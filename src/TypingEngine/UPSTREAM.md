# Typing engine: where it comes from

Code from [nyttastudio/araso](https://github.com/nyttastudio/araso), MIT (`LICENSE`), tag `windows-beta-4`,
commit `e9d5955` (0.5.1). Its data carries its own licenses (`third-party/`). Everything here ships with Orbit's
notices, built by `tools/typing-engine/build.ps1`.

Only the decision engine is used: `ArasoCore`, `ArasoService` and `ArasoBridge`, built into `TypingEngine.dll` and
called from Orbit's own process (`src/Orbit.Core/Typing/EngineBridge.cs`). Module names stay as upstream's.

**Left out:** the macOS app, the Windows text service, host and installer, Linux, release scripts, media, and
upstream's `CLAUDE.md` and `AGENTS.md`.

**Changed from the base:**

- `Sources/ArasoBridge/Bridge.swift`: the C exports are named `engine_*`.
- Added: this file, `.gitattributes`, `third-party/` (copied from upstream's `scripts/third-party`).
