# Araso in Orbit

Code from [nyttastudio/araso](https://github.com/nyttastudio/araso), MIT (`LICENSE`). The Korean frequency list is CC BY-SA 4.0.

Only the decision engine is used: `ArasoCore`, `ArasoService` and `ArasoBridge`, built into `ArasoBridge.dll` and called from Orbit's own process (`src/Orbit.Core/Typing/EngineBridge.cs`). Orbit is not an input method and registers nothing with Windows.

**Base:** tag `windows-beta-4`, commit `e9d5955` (0.5.1). The pin is in `tools/upstream/upstreams.json`.

**Left out:** the macOS app (`Sources/Araso`; `Package.swift` builds it only on macOS), the Windows text service, host and installer (`windows/`), Linux, release scripts, media, and upstream's `CLAUDE.md` and `AGENTS.md`.

**Changed from the base:** nothing in upstream's files. Added: this file and `.gitattributes`. List every change here as it is made.
