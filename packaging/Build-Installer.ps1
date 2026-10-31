# Builds the one-click installer: Builds\Release\HiveBabble-Setup-<ver>.exe
# Needs: .NET 8 SDK, Inno Setup 6 (winget install JRSoftware.InnoSetup --scope user), and a working translator\venv (Start-Translator.ps1).
[CmdletBinding()]
param([string]$Version = '1.0.0',
      [string[]]$Models = @('ja_en','translate-zh_en-1_9','ko_en','translate-es_en-1_9','translate-fr_en-1_9','translate-de_en-1_3','translate-ru_en-1_9','en_ja','en_es'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$release = Join-Path $root 'Builds\Release'; $stage = Join-Path $release 'stage'; $tr = Join-Path $stage 'translator'
& (Join-Path $root 'Build-App.ps1')
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $tr 'python'), (Join-Path $tr 'models') | Out-Null
Copy-Item (Join-Path $root 'Builds\HiveBabble.exe'), (Join-Path $root 'THIRD-PARTY-NOTICES.txt'), (Join-Path $root 'LICENSE.txt') $stage

# Embedded CPython 3.11.9 (python.org) + the site-packages of the tested venv (same 3.11 x64 ABI).
$zip = Join-Path $env:TEMP 'hbpkg\python-3.11.9-embed-amd64.zip'
if (-not (Test-Path $zip)) { New-Item -ItemType Directory -Force (Split-Path $zip) | Out-Null; Invoke-WebRequest 'https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip' -OutFile $zip }
Expand-Archive $zip (Join-Path $tr 'python') -Force
"python311.zip`r`n.`r`nLib\site-packages`r`nimport site" | Set-Content (Join-Path $tr 'python\python311._pth') -Encoding ascii
$site = Join-Path $root 'translator\venv\Lib\site-packages'
robocopy $site (Join-Path $tr 'python\Lib\site-packages') /E /NFL /NDL /NJH /NJS /XD pip __pycache__ /XF *.pyc | Out-Null
if ($LASTEXITCODE -ge 8) { throw "copying site-packages failed ($LASTEXITCODE)" }
$global:LASTEXITCODE = 0
Copy-Item (Join-Path $PSScriptRoot 'run_server.py') $tr
$pkgs = Join-Path $root 'translator\data\argos-translate\packages'
# Full license texts for every bundled Python distribution + CPython itself.
$lic = Join-Path $stage 'licenses'; New-Item -ItemType Directory -Force $lic | Out-Null
Copy-Item (Join-Path $tr 'python\LICENSE.txt') (Join-Path $lic 'Python-3.11.9-LICENSE.txt')
Get-ChildItem (Join-Path $tr 'python\Lib\site-packages') -Directory -Filter '*.dist-info' | ForEach-Object {
    $files = Get-ChildItem $_.FullName -Recurse -File | Where-Object { $_.Name -match '^(LICEN[CS]E|COPYING|NOTICE|AUTHORS)' }
    if ($files) { $d = Join-Path $lic ($_.Name -replace '\.dist-info$',''); New-Item -ItemType Directory -Force $d | Out-Null; $files | Copy-Item -Destination $d }
}
foreach ($m in $Models) { Copy-Item (Join-Path $pkgs $m) (Join-Path $tr 'models') -Recurse }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found: winget install JRSoftware.InnoSetup --scope user' }
& $iscc "/DAppVersion=$Version" "/DStage=$stage" "/DRoot=$root" "/O$release" (Join-Path $PSScriptRoot 'HiveBabble.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
Get-ChildItem $release -Filter 'HiveBabble-Setup-*.exe' | ForEach-Object { "{0}  {1:N0} MB" -f $_.FullName, ($_.Length / 1MB) }
