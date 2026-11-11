# Hive-Babble

Codex Hive Labs · live on-screen translation for Windows. **Read the world.**

## Run

```powershell
.\Start-Translator.ps1 -Background
.\Builds\HiveBabble.exe
```

The local translator runs on `http://127.0.0.1:5000`. The default setup includes English, Spanish, and Japanese. Models download on first setup; subsequent translation runs locally without an API key. Python 3.11 is required for this local service. A newly loaded model can take a few seconds on its first translation; background startup warms the Spanish/Japanese models.

## Use

- Drag the toolbar over the text and resize the frame using its edges/corners. Translation starts automatically.
- **Lock** lets clicks pass through to the app below; the toolbar stays interactive. Shortcut: **Ctrl+Alt+L**.
- **Pause / Resume** controls live updates. **Ctrl+Alt+T** forces a fresh translation when running.
- Use the language button to choose the **target** language, including English or Japanese.
- Use **Overlay**, **Side**, or **Both** to choose where results appear. The side panel includes **Copy translation**.
- The tray icon provides Show/Hide, Translate, Lock, Pause, Settings, and Exit.
- Window position and size are saved when you close the app.

## Japanese and other languages

Set Source to `auto`, Target to `en`, and OCR to `auto` for Japanese on-screen text → English. Automatic OCR selects from the Windows OCR languages installed on the machine. English and Japanese OCR are installed on the development machine; other computers need the corresponding Windows language/OCR features.

To translate English into Japanese, choose target **JA**. Translation model availability and OCR availability are separate: a translation model alone cannot recognize an unfamiliar script from the screen.

Add local translation models with, for example:

```powershell
.\Start-Translator.ps1 -Languages en,es,ja,fr -UpdateModels -Background
```

Stop the existing local translator before updating its models. The startup script reports a running service with missing languages instead of silently replacing it. The development service currently exposes additional languages beyond the three defaults; the toolbar offers a smaller common-language selection.

## Settings and cloud providers

Settings include provider, local server URL, source/target language, OCR language, and update interval (default 750 ms). DeepL and Google Cloud Translation are implemented; enter your provider's API key in Settings to use them. Keys are encrypted with Windows DPAPI for the current user. DeepL Free keys ending in `:fx` use the free API endpoint automatically.

Settings: `%APPDATA%\HiveBabble\settings.json`

Log: `%APPDATA%\HiveBabble\hivebabble.log`

Normal frame logs record counts, timings, and cache hits rather than captured text. Static captures are skipped and translated lines are cached in memory.

## Build and verify

```powershell
.\Build-App.ps1
```

This publishes a self-contained Windows x64 executable to `Builds\HiveBabble.exe`. The build script resolves the .NET 8 SDK from PATH or the user-local Microsoft installation.

Diagnostics use synthetic sample windows and write reports into the selected folder:

```powershell
.\Builds\HiveBabble.exe --verify-frame "$PWD\artifacts\m1"
.\Builds\HiveBabble.exe --verify-providers "$PWD\artifacts\m2"
.\Builds\HiveBabble.exe --verify-live "$PWD\artifacts\recheck"
.\Builds\HiveBabble.exe --verify-interactions "$PWD\artifacts\interactions"
```

See [VERIFICATION.md](VERIFICATION.md) for measured results and remaining coverage. Real cloud credentials, borderless games, and mixed-DPI multi-monitor behavior require separate validation.

## License

Hive-Babble is licensed under [Creative Commons Attribution 4.0 International (CC BY 4.0)](https://creativecommons.org/licenses/by/4.0/). See [LICENSE.txt](LICENSE.txt).

Requested attribution: "Hive-Babble by Codex Hive Labs LLC (https://codex-hive.com)"

Third-party software, including the bundled offline translator (LibreTranslate, Argos Translate, Python, and the packages and models listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)), is not licensed under CC BY 4.0. Each component remains under its own license.

## Downloads

The Windows installer and the translator source archive are available to members on the [Hive-Babble Patreon post](https://www.patreon.com/Codex_Hive_Labs/posts/hive-babble-is-173935107). This repository does not include those files.
