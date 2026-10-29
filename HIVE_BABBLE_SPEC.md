# Hive-Babble — Codex Build Spec (v0.1)
Codex Hive Labs · Windows desktop app · live on-screen translation window

## Goal
A resizable, always-on-top, see-through frame. Whatever is under the frame is captured, OCR'd, translated, and the translation is drawn over the original text inside the frame (like Google Translate camera mode, but for the screen).

## Stack
- C# / .NET 8, WPF, single project `HiveBabble` (package name com.codexhive.babble)
- Capture: Windows.Graphics.Capture (fallback: GDI BitBlt of the frame rect)
- OCR: Windows.Media.Ocr (built-in, offline). Interface `IOcrEngine` so Google Vision can be added later.
- Translate: interface `ITranslator` with `DeepLTranslator`, `GoogleTranslator`, `LibreTranslator` (self-host/Argos). API key in settings, stored with DPAPI.

## Milestones
**M1 – Frame:** borderless transparent window, resize handles, drag bar with buttons [Pause] [Lang ▾] [Mode ▾] [⚙] [×]. Click-through interior when locked (WS_EX_TRANSPARENT toggle). Excluded from its own capture (SetWindowDisplayAffinity WDA_EXCLUDEFROMCAPTURE).
**M2 – Snap & translate:** hotkey Ctrl+Alt+T captures the frame rect → OCR → translate → show results in a side panel. Copy button.
**M3 – Live overlay:** loop every 750 ms (configurable); hash the captured bitmap and skip if unchanged; draw each translated line in a filled box at its OCR bounding rect, font sized to fit.
**M4 – Polish:** translation cache (source text+lang → result), auto-detect source language, target language picker, modes (Overlay / Side panel / Both), saved window position & size, tray icon, DPI-aware multi-monitor.

## Acceptance tests
1. Frame resizes and stays on top over a browser, a game in borderless mode, and a PDF.
2. Spanish webpage text → English overlay appears within 1.5 s; static text does not re-call the API (cache hit logged).
3. Overlay never appears in its own capture (no feedback loop).
4. Works at 100% and 150% display scaling.
5. Missing/invalid API key shows a clear message, no crash.

## Structure
/HiveBabble
  App.xaml, FrameWindow.xaml(.cs), OverlayRenderer.cs, SettingsWindow.xaml
  Capture/ScreenCapturer.cs
  Ocr/IOcrEngine.cs, WindowsOcrEngine.cs
  Translate/ITranslator.cs, DeepLTranslator.cs, GoogleTranslator.cs, LibreTranslator.cs, TranslationCache.cs
  Settings/AppSettings.cs
  Build-App.ps1 (dotnet publish -c Release -r win-x64 --self-contained → Builds\HiveBabble.exe)

## Branding
Codex Hive palette (gold #E0A526 on charcoal #1E1E1E), hexagon frame corners, tagline "Read the world."
