# Start the local LibreTranslate service used by Hive-Babble.
# Examples: .\Start-Translator.ps1 -Background
#           .\Start-Translator.ps1 -Languages en,es,fr -UpdateModels
[CmdletBinding()]
param(
    [ValidatePattern('^[a-z]{2,3}(-[A-Za-z]{2,4})?(,[a-z]{2,3}(-[A-Za-z]{2,4})?)*$')]
    [string]$Languages = 'en,es,ja',
    [ValidateRange(1024,65535)][int]$Port = 5000,
    [switch]$Background,
    [switch]$UpdateModels,
    [ValidateRange(5,600)][int]$WaitSeconds = 120
)
$ErrorActionPreference = 'Stop'
$translatorRoot = Join-Path $PSScriptRoot 'translator'
$pythonPath = Join-Path $translatorRoot 'venv\Scripts\python.exe'
$serviceUrl = "http://127.0.0.1:$Port"
$requestedLanguages = $Languages.Split(',')

function Get-TranslatorLanguages {
    try { return @(Invoke-RestMethod "$serviceUrl/languages" -TimeoutSec 2) }
    catch { return @() }
}

$available = @(Get-TranslatorLanguages)
if ($available.Count -gt 0) {
    $missing = @($requestedLanguages | Where-Object { $_ -notin $available.code })
    if ($missing.Count -gt 0) {
        throw "A translator is already running at $serviceUrl, but does not provide: $($missing -join ', '). Stop that instance, then rerun with -UpdateModels."
    }
    if ($UpdateModels) {
        throw "A translator is already running at $serviceUrl. Stop that instance before updating its models."
    }
    Write-Host "Local translator ready: $serviceUrl ($($available.code -join ', '))"
    return
}

New-Item -ItemType Directory -Path $translatorRoot -Force | Out-Null
$env:XDG_DATA_HOME = Join-Path $translatorRoot 'data'
$env:XDG_CONFIG_HOME = Join-Path $translatorRoot 'config'
$env:XDG_CACHE_HOME = Join-Path $translatorRoot 'cache'
$env:PIP_CACHE_DIR = Join-Path $translatorRoot 'cache\pip'
$env:ARGOS_PACKAGES_DIR = Join-Path $translatorRoot 'data\argos-translate\packages'
New-Item -ItemType Directory -Path $env:ARGOS_PACKAGES_DIR -Force | Out-Null
# Reuse the earlier setup's downloaded models without modifying its files.
$legacyPackages = Join-Path $env:USERPROFILE '.local\share\argos-translate\packages'
if ((Test-Path -LiteralPath $legacyPackages) -and -not (Get-ChildItem -LiteralPath $env:ARGOS_PACKAGES_DIR -Directory)) {
    Get-ChildItem -LiteralPath $legacyPackages -Directory | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $env:ARGOS_PACKAGES_DIR -Recurse
    }
}
if (-not (Test-Path -LiteralPath $pythonPath)) {
    $pythonLauncher = Get-Command py -ErrorAction SilentlyContinue
    if ($pythonLauncher) {
        & $pythonLauncher.Source -3.11 -m venv (Join-Path $translatorRoot 'venv')
    } else {
        $installedPython = Join-Path $env:LOCALAPPDATA 'Programs\Python\Python311\python.exe'
        if (-not (Test-Path -LiteralPath $installedPython)) {
            throw 'Python 3.11 is required for the local translator. Install Python 3.11, then run this script again.'
        }
        & $installedPython -m venv (Join-Path $translatorRoot 'venv')
    }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $pythonPath)) { throw 'Could not create the Python 3.11 translator environment.' }
}

& $pythonPath -c "import importlib.util, sys; sys.exit(0 if importlib.util.find_spec('libretranslate') else 1)"
if ($LASTEXITCODE -ne 0) {
    & $pythonPath -m pip install 'libretranslate==1.9.6'
    if ($LASTEXITCODE -ne 0) { throw 'LibreTranslate installation failed. Check the pip output above and retry.' }
}

# Keep translated screen text out of verbose Argos diagnostic logs.
$env:ARGOS_DEBUG = '0'
$env:PYTHONUNBUFFERED = '1'
$runtimeEntryPath = Join-Path $translatorRoot 'run_server.py'
@'
import logging
from pathlib import Path
import minisbd.models

# MiniSBD uses LOCALAPPDATA on Windows unless its module cache is configured.
minisbd.models.cache_dir = str(Path(__file__).resolve().parent / 'cache' / 'minisbd')
from libretranslate.main import main
logging.getLogger('argostranslate.utils').setLevel(logging.WARNING)
main()
'@ | Set-Content -LiteralPath $runtimeEntryPath -Encoding utf8
$missingModels = & $pythonPath -c "import sys; import argostranslate.package as p; installed={c for x in p.get_installed_packages() for c in (x.from_code,x.to_code)}; print('yes' if set(sys.argv[1].split(','))-installed else 'no')" $Languages
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect local translation models.' }
$serverArguments = @('--host', '127.0.0.1', '--port', "$Port", '--load-only', $Languages)
if ($UpdateModels -or $missingModels -eq 'yes') { $serverArguments += '--update-models' }
# Normal starts reuse installed models; adding a language downloads its missing models.
if (-not $Background) {
    Write-Host "Starting local translator at $serviceUrl. Keep this window open; Ctrl+C stops it."
    & $pythonPath $runtimeEntryPath @serverArguments
    if ($LASTEXITCODE -ne 0) { throw "Local translator exited with code $LASTEXITCODE." }
    return
}

$stdoutPath = Join-Path $translatorRoot "server-$Port.log"
$stderrPath = Join-Path $translatorRoot "server-$Port.err.log"
$launchArguments = @(('"' + $runtimeEntryPath + '"')) + $serverArguments
$serverProcess = Start-Process -FilePath $pythonPath -ArgumentList $launchArguments -WorkingDirectory $PSScriptRoot `
    -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru
$serverProcess.Id | Set-Content -LiteralPath (Join-Path $translatorRoot "server-$Port.pid") -Encoding ascii
$deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
do {
    $available = @(Get-TranslatorLanguages)
    if ($available.Count -gt 0) {
        $missing = @($requestedLanguages | Where-Object { $_ -notin $available.code })
        if ($missing.Count -gt 0) { throw "Translator started but the requested models are missing: $($missing -join ', '). See $stderrPath." }
        Write-Host 'Warming translation models for the first screen capture...'
        $warmups = @()
        if ('es' -in $requestedLanguages -and 'en' -in $requestedLanguages) { $warmups += @{ q='Hola.'; source='es'; target='en'; format='text' } }
        if ('ja' -in $requestedLanguages -and 'en' -in $requestedLanguages) {
            $warmups += @{ q='こんにちは。'; source='ja'; target='en'; format='text' }
            $warmups += @{ q='Hello.'; source='en'; target='ja'; format='text' }
        }
        foreach ($warmup in $warmups) {
            $jsonBytes = [Text.Encoding]::UTF8.GetBytes(($warmup | ConvertTo-Json -Compress))
            Invoke-RestMethod "$serviceUrl/translate" -Method Post -ContentType 'application/json; charset=utf-8' -Body $jsonBytes -TimeoutSec 60 | Out-Null
        }
        Write-Host "Local translator ready: $serviceUrl ($($available.code -join ', ')); process $($serverProcess.Id)."
        return
    }
    $serverProcess.Refresh()
    if ($serverProcess.HasExited) { throw "Local translator exited with code $($serverProcess.ExitCode). See $stderrPath." }
    Start-Sleep -Milliseconds 500
} while ([DateTime]::UtcNow -lt $deadline)
throw "Translator is still starting at $serviceUrl. First setup may download models. Check $stdoutPath and $stderrPath; retry this script when setup finishes."
