# Hive-Babble
Codex Hive Labs · live on-screen translation window. *Read the world.*

## Build
`powershell -ExecutionPolicy Bypass -File .\Build-App.ps1` → `Builds\HiveBabble.exe`

## Use (M2)
- Drag the gold bar to move, drag the edges to resize. Lock makes the frame click-through (bar stays clickable; Ctrl+Alt+L also toggles).
- **Ctrl+Alt+T** captures what's inside the frame, OCRs it (Windows OCR, offline), translates it, and shows it in the side panel. **Copy** copies the result.
- Log: `%APPDATA%\HiveBabble\hivebabble.log`. Settings: `%APPDATA%\HiveBabble\settings.json` (⚙ button).

## Start the local translator (free, offline, no key)
Hive-Babble uses LibreTranslate (Argos models) running on your own PC at `http://127.0.0.1:5000`.
```
powershell -ExecutionPolicy Bypass -File .\Start-Translator.ps1
```
- First run creates a Python 3.11 venv in `translator\` and downloads only the English↔Spanish models. Leave the window open while you translate.
- More languages: `.\Start-Translator.ps1 -Languages en,es,fr,ro`
- If it isn't running, Hive-Babble says "Local translator not running…" instead of crashing.

## DeepL (later)
Open ⚙ Settings, set Translator to **DeepL**, paste the key (stored encrypted with Windows DPAPI, never in the repo). Free keys ending in `:fx` use api-free.deepl.com automatically.

## OCR languages
Windows OCR uses the language packs installed in Windows. For accurate Spanish (ñ, accents) add Spanish in Settings → Time & language → Language & region, then set `OcrLanguage` to `es-ES` in settings.json.