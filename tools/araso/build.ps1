# Builds Araso's decision engine from src/Araso and gathers what ships into artifacts/araso-stage.
# Orbit calls ArasoBridge.dll from its own process (src/Orbit.Core/Typing/EngineBridge.cs). Registers
# nothing. Needs the Swift toolchain (winget install Swift.Toolchain). The tests in TypingEngineTests
# then run against the stage.
#   powershell -File tools/araso/build.ps1
param(
    # Short on purpose: a dependency's submodules go past Windows' 260 characters under a deep folder.
    [string]$SwiftScratch = "$env:USERPROFILE\.oa-swift"
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path
$source = "$repo\src\Araso"
$stage = "$repo\artifacts\araso-stage"

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
Write-Host "Araso $version"

Push-Location $source
try {
    Invoke-Checked 'Swift bridge' { swift build -c release --product ArasoBridge --scratch-path $SwiftScratch }
} finally {
    Pop-Location
}

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage, "$stage\Resources" | Out-Null
Copy-Item "$SwiftScratch\out\Products\Release-windows-x86_64\ArasoBridge.dll" $stage
$core = Get-ChildItem "$env:LOCALAPPDATA\Programs\Swift", "$env:ProgramFiles\Swift", 'C:\Library' -Recurse -Filter swiftCore.dll -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match 'Runtimes' -and $_.FullName -notmatch 'arm64|aarch64|i686|x86\\' } | Select-Object -First 1
if (-not $core) { throw 'swiftCore.dll not found' }
Get-ChildItem $core.DirectoryName -Filter *.dll | Copy-Item -Destination $stage
# Japanese is off (no kanji dictionary, no kana or kanji-ranking model) and the engine's own UI text isn't shown.
Copy-Item "$source\Sources\ArasoCore\Data\*.txt" "$stage\Resources" -Exclude 'kana_trigrams.txt', 'text_trigrams.txt'
# The Swift runtime needs the Visual C++ runtime, which a fresh Windows may not have.
foreach ($dll in 'vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll') { Copy-Item "$env:WINDIR\System32\$dll" $stage }

# What the build looks for to know the engine is there. No BOM, no newline to trim wrongly.
[IO.File]::WriteAllText("$stage\version.txt", $version)

$size = (Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Stage: {0:N0} MB, {1} files" -f ($size / 1MB), (Get-ChildItem $stage -Recurse -File).Count)
