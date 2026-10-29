# Builds a self-contained single-file Hive-Babble to Builds\HiveBabble.exe
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'Install the .NET 8 SDK: winget install Microsoft.DotNet.SDK.8' }
& $dotnet publish (Join-Path $root 'HiveBabble\HiveBabble.csproj') -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $root 'Builds')
if ($LASTEXITCODE -ne 0) { throw "publish failed ($LASTEXITCODE)" }
Write-Host "Built: $(Join-Path $root 'Builds\HiveBabble.exe')"
