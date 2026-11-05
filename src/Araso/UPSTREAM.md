# Araso in Orbit

Code from [nyttastudio/araso](https://github.com/nyttastudio/araso), MIT (`LICENSE`). The Korean frequency list is CC BY-SA 4.0.

**Base:** tag `windows-beta-4`, commit `e9d5955` (0.5.1). The pin is in `tools/upstream/upstreams.json`.

**Left out:** the macOS app (`Sources/Araso`; `Package.swift` builds it only on macOS), Linux, release scripts, media, and upstream's `CLAUDE.md` and `AGENTS.md`.

**Changed from the base:** nothing in upstream's files. Added: this file and `.gitattributes`. `windows/installer/Araso.iss` isn't used: `src/Orbit.Core/Araso/ArasoSetup.cs` does its job. List every change here as it is made.

- `windows/tip/TextService.cpp` (`Floats`, `OnCompositionTerminated`): Chromium's windows compose in the text like any TSF app, although its text store calls every context transitory, which sent Chrome, Edge and Electron apps to `WordWindow`. A candidate to send upstream.
- `windows/host/Ui.cpp`, `Ui.h` (`WordWindow`): the word drawn at the caret in classic apps takes the app's own background, read from the screen when the word starts, with black or white text to suit, instead of the Windows theme's color, which showed as a band.
