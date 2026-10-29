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
