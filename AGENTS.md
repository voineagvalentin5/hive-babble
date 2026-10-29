# Hive-Babble — start here (for Codex / Claude Code)

Owner: Valentin Voineag, Codex Hive Labs. R&D reviewer: Gimi.
Spec: `HIVE_BABBLE_SPEC.md` in this folder. Build milestones in order: M1, M2, M3, M4. Don't start a milestone until the previous one passes its checks.

## Before you start
- This machine has NO .NET SDK yet (`dotnet` not found, checked Oct 29, 2026). Install the .NET 8 SDK first (`winget install Microsoft.DotNet.SDK.8`) and confirm `dotnet --list-sdks` shows 8.x.
- M1 needs no API key. M2+ translation needs a DeepL or Google key from Valentin. Never commit a key: it goes in the app settings and is stored with DPAPI.
- Work only in this folder. Commit after each milestone with a clear message.

## Done means
- M1: frame resizes, drags, stays on top, locks to click-through, and is excluded from its own capture. Show a screenshot.
- Report what you checked live and what you couldn't check.
