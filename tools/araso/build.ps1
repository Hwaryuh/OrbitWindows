# Builds Araso from src/Araso and gathers what ships into artifacts/araso-stage, in the order of
# upstream's windows.yml. It registers nothing: the last step only loads the models and asks them
# to decide. Needs the Swift toolchain (winget install Swift.Toolchain), CMake and MSVC.
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
Invoke-Checked 'Configure x64' { cmake -S "$source\windows" -B "$repo\artifacts\araso-build" -A x64 "-DARASO_VERSION=$version" }
Invoke-Checked 'Build x64' { cmake --build "$repo\artifacts\araso-build" --config Release }
# 32-bit apps load the text service too; the host stays 64-bit.
Invoke-Checked 'Configure x86' { cmake -S "$source\windows" -B "$repo\artifacts\araso-build-x86" -A Win32 "-DARASO_VERSION=$version" -DARASO_HOST=OFF }
Invoke-Checked 'Build x86' { cmake --build "$repo\artifacts\araso-build-x86" --config Release }

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage, "$stage\x86", "$stage\Resources\ko.lproj" | Out-Null
Copy-Item "$SwiftScratch\out\Products\Release-windows-x86_64\ArasoBridge.dll" $stage
$core = Get-ChildItem "$env:LOCALAPPDATA\Programs\Swift", "$env:ProgramFiles\Swift", 'C:\Library' -Recurse -Filter swiftCore.dll -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match 'Runtimes' -and $_.FullName -notmatch 'arm64|aarch64|i686|x86\\' } | Select-Object -First 1
if (-not $core) { throw 'swiftCore.dll not found' }
Get-ChildItem $core.DirectoryName -Filter *.dll | Copy-Item -Destination $stage
Copy-Item "$source\Sources\ArasoCore\Data\*.txt" "$stage\Resources"
Copy-Item -Recurse "$SwiftScratch\checkouts\AzooKeyKanaKanjiConverter\Sources\KanaKanjiConverterModuleWithDefaultDictionary\azooKey_dictionary_storage\Dictionary" "$stage\Resources\Dictionary"
Copy-Item "$source\Resources\ko.lproj\Localizable.strings", "$source\windows\Resources\ko.lproj\Windows.strings" "$stage\Resources\ko.lproj"
Copy-Item "$repo\artifacts\araso-build\Release\ArasoHost.exe", "$repo\artifacts\araso-build\Release\ArasoTip.dll" $stage
Copy-Item "$repo\artifacts\araso-build-x86\Release\ArasoTip.dll" "$stage\x86"
# The Swift runtime needs the Visual C++ runtime, which a fresh Windows may not have.
foreach ($dll in 'vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll') { Copy-Item "$env:WINDIR\System32\$dll" $stage }

# What Orbit reads to know this build carries Araso. No BOM, no newline to trim wrongly.
[IO.File]::WriteAllText("$stage\version.txt", $version)

$size = (Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Stage: {0:N0} MB, {1} files" -f ($size / 1MB), (Get-ChildItem $stage -Recurse -File).Count)

$test = Start-Process "$stage\ArasoHost.exe" -ArgumentList '--selftest' -Wait -PassThru
if ($test.ExitCode -ne 0) { throw "Araso's self-test failed ($($test.ExitCode))" }
Write-Host 'Self-test passed: every model loaded, "hello" is English and "dkssud" is 안녕'
