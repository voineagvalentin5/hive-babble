# Builds Builds\Release\HiveBabble-translator-sources.zip: corresponding source for the copyleft (AGPL/GPL/LGPL) Python
# components shipped unmodified in the Hive-Babble translator bundle, plus their licenses and rebuild notes.
[CmdletBinding()] param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$py = Join-Path $root 'translator\venv\Scripts\python.exe'
$release = Join-Path $root 'Builds\Release'; $work = Join-Path $release 'sources-stage'
$pkgs = 'libretranslate==1.9.6','argos-translate-files==1.4.4','LexiLang==1.0.7','minisbd==0.9.5','pymupdf==2.0','pymupdf4llm==2.0','pymupdf_layout==2.0','pysrt==1.1.2','chardet==7.7.0'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
$src = Join-Path $work 'sdist'; New-Item -ItemType Directory -Force $src | Out-Null
foreach ($p in $pkgs) {
    # Straight from PyPI's JSON API (no build step): the sdist, or for pure-Python packages without one, the source wheel.
    $name, $ver = $p -split '=='
    $info = Invoke-RestMethod "https://pypi.org/pypi/$name/$ver/json"
    $f = @($info.urls | Where-Object packagetype -eq 'sdist') + @($info.urls | Where-Object { $_.filename -like '*-none-any.whl' }) | Select-Object -First 1
    if (-not $f) { Write-Warning "no source distribution for $p"; continue }
    Invoke-WebRequest $f.url -OutFile (Join-Path $src $f.filename) -UseBasicParsing
    $h = (Get-FileHash (Join-Path $src $f.filename) -Algorithm SHA256).Hash.ToLower()
    if ($h -ne $f.digests.sha256) { throw "hash mismatch for $($f.filename)" }
    "{0}  sha256 {1}  {2}" -f $f.filename, $h, $f.url | Add-Content (Join-Path $work 'SHA256SUMS.txt')
}
$lic = Join-Path $work 'licenses'; New-Item -ItemType Directory -Force $lic | Out-Null
$site = Join-Path $root 'translator\venv\Lib\site-packages'
foreach ($p in $pkgs) {
    $n = ($p -split '==')[0] -replace '-','_'
    Get-ChildItem $site -Directory -Filter '*.dist-info' | Where-Object { $_.Name -like "$n-*" -or $_.Name -like ("$n-*" -replace '_','-') } | ForEach-Object {
        $d = Join-Path $lic ($_.Name -replace '\.dist-info$',''); New-Item -ItemType Directory -Force $d | Out-Null
        Get-ChildItem $_.FullName -Recurse -File | Where-Object { $_.Name -match '^(LICEN[CS]E|COPYING|NOTICE|AUTHORS)' -or $_.Name -eq 'METADATA' } | Copy-Item -Destination $d
    }
}
$glue = Join-Path $work 'hive-babble-glue'; New-Item -ItemType Directory -Force $glue | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'run_server.py'), (Join-Path $PSScriptRoot 'Build-Installer.ps1'), $MyInvocation.MyCommand.Path $glue
& $py -m pip freeze --all | Set-Content (Join-Path $work 'requirements-frozen.txt') -Encoding utf8
Copy-Item (Join-Path $PSScriptRoot 'SOURCES-README.txt') (Join-Path $work 'README.txt')
$zip = Join-Path $release 'HiveBabble-translator-sources.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $work '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $work -Recurse -Force
"{0}  {1:N1} MB" -f $zip, ((Get-Item $zip).Length / 1MB)