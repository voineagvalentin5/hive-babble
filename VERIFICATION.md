# Hive-Babble verification

Validation is performed in milestone order. A build or an implemented feature is not a substitute for a live acceptance check.

## Environment

- Windows desktop, 125% scaling in the current session.
- .NET SDK 8.0.427 is installed at `%LOCALAPPDATA%\Microsoft\dotnet`.
- The shell did not expose `dotnet` on PATH; `Build-App.ps1` resolves the user-local installation.
- The initial release build passed with zero warnings and errors.

## Reproducible frame checks

Launch the built executable with `--verify-frame <absolute-output-directory>`.
The diagnostic writes native-window checks to `frame-verification.json`, actual capture pixels to `capture-*.png`, and a labeled WPF visual composite to `frame-render-preview.png`.

The visual composite is explicitly a render preview: Windows capture protection intentionally hides the running frame from screenshot tools. The capture files verify the underlying content independently.

Live Windows UI checks performed separately: application startup and Ctrl+Alt+L lock toggle.

M1 passed on 2026-10-29 after a pending elevation prompt was dismissed. All 12 native-window/capture checks pass in `artifacts/m1/frame-verification.json`. Before/after captures contain identical underlying pixels and zero pixels from the opaque magenta overlay probe. The independent background is capturable.

## Checks still requiring separate coverage

- Physical mouse dragging/resizing and clicking through over each target application.
- Browser, PDF viewer, and an actual borderless game.
- 100% and 150% display scaling and mixed-DPI monitor transitions. The current desktop is 125%.

## M2

The real synthetic Spanish screen → Windows OCR → local LibreTranslate → side panel → Copy path passed all five checks in `artifacts/m2/translation-verification.json`. The first full operation took 4074 ms; the 1.5-second live target is not yet established. The sample was correctly recognized as three Spanish lines and translated to English.

All 15 provider checks passed in `artifacts/m2/provider-verification.json`, including missing/invalid credentials, request format, disposal, timeout/offline/quota responses, redaction, and an in-memory DPAPI roundtrip. Cloud HTTP responses were intercepted for those checks; real Google/DeepL account access remains untested.

## M3/M4 + Japanese live verification (2026-10-29 20:52 ET, `HiveBabble.exe --verify-live`, 125% DPI, single monitor)
Translator restarted with `Start-Translator.ps1 -Languages en,ja,zh,ko,es,fr,de,pt,it,ru,ar,hi,tr,pl,nl,uk,id,vi,th,sv,cs -UpdateModels`:
21 languages / 42 models loaded (en, ar, zh-Hans, cs, nl, fr, de, hi, id, it, ja, ko, pl, pt, ru, es, sv, th, tr, uk, vi). OCR installed: en-US, ja.
- PASS es->en overlay: "Hey, world. | The cat sleeps in the house. | Thanks for reading with us."; detected=es; 497 ms from loop start
- PASS boxes = OCR lines, DPI-correct position, font fits; no feedback loop (with-overlay capture == without; 5/5 static frames skipped)
- PASS cache 3/3 lines; target picker en->ja; Overlay/Side/Both; tray (7 items); saved placement restored on relaunch
- PASS clear errors without crash: translator down, bad URL, DeepL no/bad key, Google no key; recovery
- PASS Japanese on screen -> English: OCR auto-picked ja; read "こんにちは、世界。 | 猫は家の中で寝ています。 | 読んでくれてありがとう。";
  overlay "Hello world. | The cat is sleeping in the house. | Thank you for reading。"; detected=ja; 127 ms from loop start
- Note: first request per language after a translator restart is slow (es 5.6 s, ja 2.1 s cold); startup warm-up now primes es/ja/zh/ko/fr/de (~11 s, background).
- NOT VERIFIED: mixed-DPI multi-monitor, real DeepL key, real games/apps, zh/ko on-screen OCR (no packs installed).

## Independent recheck and interaction fixes (2026-10-30)

The current implementation passed all 20 real local live-flow checks again at 125% scaling. The warmed Spanish overlay appeared in 370 ms and Japanese in 113 ms. Japanese OCR selected `auto:ja` and translated the synthetic Japanese sample correctly. See `artifacts/recheck/live-verification.json`.

Seven additional regression checks passed in `artifacts/interactions/interaction-verification.json`. They verify redraw after moving away and back to identical pixels; suppression of outstanding results after Pause, Hide, and settings changes; cleared caches staying cleared after an old response; recovery with new settings; and provider cooldown despite a changing screen. These checks use real capture/OCR and deliberately delayed synthetic translations.

The fixes invalidate pending results when the frame or settings change, clear provider caches on settings changes, and apply error cooldown independently of frame hashes. Overlay boxes now use an opaque fill so original characters do not show through the translation. The self-contained build is refreshed in `Builds/HiveBabble.exe`.

`artifacts/recheck/japanese-live.png` is an actual desktop screenshot of the synthetic Japanese scene and live English overlay. The diagnostic stops processing, briefly enables capture of its own frame/toolbar, takes the screenshot, and immediately restores capture exclusion. Normal app operation retains exclusion, which the live test verifies separately.

Remaining limits: cloud account access and real game/browser/PDF interaction are not covered by this recheck. Mixed-DPI alignment remains unverified; saved placement also still needs monitor-specific handling for gaps between monitors.
