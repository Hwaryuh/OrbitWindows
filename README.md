# Orbit for Windows

A Windows port of [Orbit](https://github.com/3x-haust/Orbit) by 3x-haust, the all-in-one Mac utility app.
This is an unofficial project, not made by or affiliated with the original author.

It is a new program in C# (.NET 10, WPF) that follows the original's behavior and design; no Swift code was
carried over. Ported features follow Orbit v1.4.8.

## What it has

- **Ring**: hold the middle mouse button for a ring of actions under the pointer.
- **Launcher** (Alt+Space): programs, open windows, files, Orbit commands, Windows settings, math, currency,
  units, weather, world time and listening ports.
- **Capture**: area, window, screen and text (OCR, QR codes), with an editor, pins and a library.
- **Clipboard** history, link cleaning, **Translate** (Ctrl+C twice) and **Snippets**.
- **Korean/English switching** as you type, from Orbit's own key hook (no input method to register).
- **Keep Awake**, per-app **Sound**, **System**, **Ports**, **Displays** (DDC/CI) and a **Scratchpad**.

Visible text is in Korean and English.

## Install

Download `OrbitWindows-win-Setup.exe` from [Releases](../../releases) and run it. It installs for the current
user (no admin) and updates itself from the releases here. The installer is not signed yet, so Windows
SmartScreen may warn before it runs.

## Build

Needs the .NET 10 SDK, and Swift 6.4.1 for Windows for the typing engine.

```powershell
powershell -File tools/typing-engine/build.ps1   # the Korean/English decision engine (TypingEngine.dll)
dotnet build Orbit.slnx
dotnet test tests/Orbit.Tests
powershell -File tools/release/pack.ps1          # an installer in artifacts/releases
```

## Credits

Orbit's design, behavior and icon come from [3x-haust/Orbit](https://github.com/3x-haust/Orbit) (MIT).
Third-party code and data and their licenses are listed in
[`src/Orbit.App/THIRD-PARTY-NOTICES.txt`](src/Orbit.App/THIRD-PARTY-NOTICES.txt), which the app also opens from
its About page.
