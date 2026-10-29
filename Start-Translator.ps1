# Starts the free, offline LibreTranslate (Argos) server for Hive-Babble on 127.0.0.1:5000.
# First run sets up a Python venv in .\translator and downloads only the es<->en models.
param([string]$Languages = 'en,es')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$py = Join-Path $root 'translator\venv\Scripts\python.exe'
if (-not (Test-Path $py)) {
  py -3.11 -m venv (Join-Path $root 'translator\venv')
  & $py -m pip install --upgrade pip
  & $py -m pip install libretranslate
}
$lt = Join-Path $root 'translator\venv\Scripts\libretranslate.exe'
& $lt --host 127.0.0.1 --port 5000 --load-only $Languages --update-models