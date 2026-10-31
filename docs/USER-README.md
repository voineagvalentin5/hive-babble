# Hive-Babble

**Read the world.** Hive-Babble translates text on your screen, live. Put its frame over a game, a video, a manga page or an app in another language, and the translation appears right on top of the text or in a side panel.

Made by Codex Hive Labs LLC. Free to use.

## What you need

- Windows 10 (version 2004 / build 19041 or newer) or Windows 11, 64-bit
- About 3 GB of free disk space (app and translator about 900 MB, language models about 1.3 GB)
- An internet connection **the first time you start it**, to download the language models (about 1.1 GB). After that it works fully offline.

## Install

1. Run `HiveBabble-Setup-1.0.0.exe`. No administrator rights are needed; it installs just for you, in `%LOCALAPPDATA%\Programs\Hive-Babble`.
2. **"Windows protected your PC" warning:** the installer isn't code-signed yet, so Windows SmartScreen may warn you. Click **More info**, then **Run anyway**. Only do this if you downloaded Hive-Babble from the official Codex Hive Labs page.
3. Start Hive-Babble from the Start menu (and the desktop shortcut, if you chose one).

**First start:** Hive-Babble downloads the language models it needs and shows the progress at the bottom of the frame ("Downloading language model 2/7…"). If a download fails it retries automatically, and anything still missing is tried again the next time you start it. Japanese → English is included in the installer, so that works right away.

## Uninstall

Settings → Apps → Installed apps → **Hive-Babble** → Uninstall. This removes the app, the translator and the downloaded models. Your settings file in `%APPDATA%\HiveBabble` is left in case you reinstall; delete that folder to remove it too.

## How to use it

- **The frame:** a glowing gold frame. Move and resize it so it covers the text you want translated. Hive-Babble reads the text inside it and translates it every moment while it changes.
- **The bar** (on the frame):
  - **Lock / Unlock:** locks the frame so clicks pass through it to the app underneath. Shortcut: **Ctrl+Alt+L**.
  - **Pause / Resume:** stops and restarts live translation.
  - **EN ▾:** the language to translate into.
  - **Overlay ▾:** where translations appear: *Overlay* (on top of the original text), *Side* (in the side panel), or *Both*.
  - **⚙ Settings** and **✕ Close**.
- **Side panel:** shows the full translation as text, with **Copy translation**.
- **Translate now:** **Ctrl+Alt+T** re-reads the frame immediately.
- **Tray icon** (near the clock): Show / hide, Translate now, Lock / unlock, Pause / resume, Settings, Exit.
- **Settings:** source language (`auto` detects it for you), target language, the text-recognition (OCR) language, and how often the frame refreshes.

## Languages

Translated into English from: **Japanese, Chinese, Korean, Spanish, French, German and Russian**. English → Japanese and English → Spanish are also available offline: pick the target with **EN ▾**, then restart Hive-Babble once so it downloads that model. Other targets in the menu (Italian, Portuguese and so on) only work with the optional DeepL or Google providers in this version.

### Japanese, Chinese or Korean text isn't recognized?

Hive-Babble uses Windows' built-in text recognition, which needs that language installed in Windows:

1. Open **Settings → Time & language → Language & region**.
2. Click **Add a language**, choose *Japanese* (or *Chinese (Simplified)*, *Korean*), and click **Next**.
3. Make sure the **Optical character recognition** feature (part of the basic typing / language pack) is checked. You don't need to make it your display language.
4. Click **Install**, wait for it to finish, then restart Hive-Babble.

## Privacy

- **Everything runs on your PC.** Text recognition uses Windows, and translation uses the offline translator that comes with Hive-Babble. Your screen and its text are never sent anywhere.
- **No screenshots are kept.** Frames are read in memory and thrown away.
- **What is stored**, all on your PC: settings in `%APPDATA%\HiveBabble\settings.json` (window position, languages, options; an optional DeepL API key is encrypted with Windows' own protection), a log file `hivebabble.log` (timings, counts and errors only, never the text it reads), and the translator's models and cache.
- The internet is only used to download language models, and by the optional DeepL or Google providers if you choose one in Settings (those send the text to that service).

## Troubleshooting

- **"Starting offline translator…" stays for a long time:** the first start can take up to a minute on slower PCs. If it says it stopped unexpectedly, restart Hive-Babble.
- **"Couldn't download language models":** check your internet connection (and any firewall blocking `argos-net.com` / `raw.githubusercontent.com`), then restart Hive-Babble. It picks up where it left off.
- **Nothing is translated:** make sure the frame covers the text, it isn't paused, and the text's language is installed in Windows (see above). Very small or stylized text may not be recognized; try making the window larger.
- **A shortcut doesn't work:** another app may already use Ctrl+Alt+L or Ctrl+Alt+T. The tray menu does the same things.
- **Still stuck?** Email us and attach `%APPDATA%\HiveBabble\hivebabble.log` (it contains no screen text).

## Credits and licenses

Hive-Babble is free proprietary software from Codex Hive Labs LLC; see `LICENSE.txt`. It includes open-source components, most notably the LibreTranslate / Argos Translate offline translator (AGPL-3.0) and OPUS-MT based language models (CC-BY 4.0). Full credits and license texts are in `THIRD-PARTY-NOTICES.txt` and the `licenses` folder, and the source code for the copyleft parts is offered as `HiveBabble-translator-sources.zip` next to the download.

## Contact

Codex Hive Labs LLC · voineagvalentin@codex-hive.com
