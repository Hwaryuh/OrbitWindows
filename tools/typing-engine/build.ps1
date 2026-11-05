# Builds the typing engine from src/TypingEngine and gathers what ships into artifacts/typing-engine-stage.
# Orbit calls TypingEngine.dll from its own process (src/Orbit.Core/Typing/EngineBridge.cs). Registers
# nothing. Needs the Swift toolchain (winget install Swift.Toolchain). The tests in TypingEngineTests
# then run against the stage.
#   powershell -File tools/typing-engine/build.ps1
param(
    # Short on purpose: a dependency's submodules go past Windows' 260 characters under a deep folder.
    [string]$SwiftScratch = "$env:USERPROFILE\.oa-swift"
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path
$source = "$repo\src\TypingEngine"
$stage = "$repo\artifacts\typing-engine-stage"

function Invoke-Checked([string]$What, [scriptblock]$Command) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    # Native tools write progress to stderr: only their exit code says whether they failed.
    $ErrorActionPreference = 'Continue'
    & $Command 2>&1 | Where-Object { "$_" -match 'error|fatal' } | ForEach-Object { Write-Host "  $_" }
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($code -ne 0) { throw "$What failed ($code)" }
    Write-Host "$What done in $([int]$clock.Elapsed.TotalSeconds) s"
}

# A shell opened before the install doesn't have these yet. Long paths for this run only.
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + [Environment]::GetEnvironmentVariable('Path', 'Machine')
if (-not $env:SDKROOT) { $env:SDKROOT = [Environment]::GetEnvironmentVariable('SDKROOT', 'User') }
$env:GIT_CONFIG_COUNT = '1'
$env:GIT_CONFIG_KEY_0 = 'core.longpaths'
$env:GIT_CONFIG_VALUE_0 = 'true'

$plist = Get-Content "$source\Resources\Info.plist" -Raw
$version = [regex]::Match($plist, '<key>CFBundleShortVersionString</key>\s*<string>([^<]+)</string>').Groups[1].Value
Write-Host "Typing engine $version"

Push-Location $source
try {
    Invoke-Checked 'Swift bridge' { swift build -c release --product ArasoBridge --scratch-path $SwiftScratch }
} finally {
    Pop-Location
}

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage, "$stage\Resources" | Out-Null
Copy-Item "$SwiftScratch\out\Products\Release-windows-x86_64\ArasoBridge.dll" "$stage\TypingEngine.dll"
$core = Get-ChildItem "$env:LOCALAPPDATA\Programs\Swift", "$env:ProgramFiles\Swift", 'C:\Library' -Recurse -Filter swiftCore.dll -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match 'Runtimes' -and $_.FullName -notmatch 'arm64|aarch64|i686|x86\\' } | Select-Object -First 1
if (-not $core) { throw 'swiftCore.dll not found' }
Get-ChildItem $core.DirectoryName -Filter *.dll | Copy-Item -Destination $stage
# Japanese is off (no kanji dictionary, no kana or kanji-ranking model) and the engine's own UI text isn't shown.
Copy-Item "$source\Sources\ArasoCore\Data\*.txt" "$stage\Resources" -Exclude 'kana_trigrams.txt', 'text_trigrams.txt'
# The Swift runtime needs the Visual C++ runtime, which a fresh Windows may not have.
foreach ($dll in 'vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll') { Copy-Item "$env:WINDIR\System32\$dll" $stage }

# Every license the engine's code and data need, beside it. A Swift package without a license file stops the build.
$notices = New-Object System.Text.StringBuilder
[void]$notices.AppendLine("Orbit's typing engine includes the following software and data.")
function Add-Section([string]$Title, [string[]]$Lines) {
    [void]$notices.AppendLine().AppendLine("==== $Title ====").AppendLine()
    foreach ($line in $Lines) { [void]$notices.AppendLine($line) }
}
function Add-Notice([string]$Title, [string]$File) {
    if (-not (Test-Path $File)) { throw "No license text for $Title ($File)" }
    Add-Section $Title @((Get-Content $File -Raw -Encoding UTF8).TrimEnd())
}
Add-Notice 'The engine (nyttastudio/araso, MIT)' "$source\LICENSE"
foreach ($checkout in Get-ChildItem "$SwiftScratch\checkouts" -Directory) {
    $license = Get-ChildItem $checkout.FullName -File | Where-Object { $_.Name -match '^(license|copying)' } | Select-Object -First 1
    if (-not $license) { throw "No license file in $($checkout.Name)" }
    Add-Notice "$($checkout.Name) (Swift package)" $license.FullName
}
Add-Notice 'llama.cpp (linked through AzooKeyKanaKanjiConverter)' "$source\third-party\llama.cpp-LICENSE"
Add-Notice 'Tatoeba (Korean syllable and word counts)' "$source\third-party\tatoeba.txt"
Add-Notice 'SCOWL (english_words.txt, english_spelling.txt, english_acronyms.txt)' "$source\third-party\scowl-Copyright"
Add-Notice 'FrequencyWords (korean_vocabulary.txt)' "$source\third-party\frequencywords.txt"
Add-Section 'Swift runtime' @(
    'The Swift runtime and Foundation libraries beside the engine (swift*.dll, Foundation*.dll, dispatch.dll, BlocksRuntime.dll):',
    'Apache License 2.0 with Runtime Library Exception, https://swift.org/LICENSE.txt.',
    '_FoundationICU.dll carries ICU: Unicode License V3, https://github.com/unicode-org/icu/blob/main/LICENSE.')
Add-Section 'Microsoft Visual C++ runtime' @(
    'vcruntime140*.dll, msvcp140*.dll and concrt140.dll: the Microsoft Visual C++ Redistributable, redistributed under',
    'its license terms, https://visualstudio.microsoft.com/license-terms/.')
[IO.File]::WriteAllText("$stage\NOTICES.txt", $notices.ToString())

# What the build looks for to know the engine is there. No BOM, no newline to trim wrongly.
[IO.File]::WriteAllText("$stage\version.txt", $version)

$size = (Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Stage: {0:N0} MB, {1} files" -f ($size / 1MB), (Get-ChildItem $stage -Recurse -File).Count)
