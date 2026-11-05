# Builds an installer on this PC: artifacts/releases/OrbitWindows-win-Setup.exe. Running it installs Orbit for this
# user (no admin), with a Start menu entry and a start at sign-in. A newer one run later updates the install.
#   powershell -File tools/release/pack.ps1
# Each run takes the next version after the last one packed here, as Velopack only updates to a higher version.
# The pack id is not "Orbit": Velopack installs to %LOCALAPPDATA%\<pack id>, and Orbit keeps captures, clips and
# logs in %LOCALAPPDATA%\Orbit, which an uninstall would take with it.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path
$packId = 'OrbitWindows'
$publish = "$repo\artifacts\publish"
$releases = "$repo\artifacts\releases"
$stage = "$repo\artifacts\typing-engine-stage"

if (-not (Test-Path "$stage\version.txt")) { throw 'The typing engine is not built: run tools/typing-engine/build.ps1 first.' }

# The version: one past the highest packed here.
$version = '0.1.1'
$feed = "$releases\releases.win.json"
if (Test-Path $feed) {
    $last = (Get-Content $feed -Raw | ConvertFrom-Json).Assets | ForEach-Object { [version]$_.Version } | Sort-Object | Select-Object -Last 1
    if ($last) { $version = "$($last.Major).$($last.Minor).$($last.Build + 1)" }
}
Write-Host "Orbit $version"

function Invoke-Checked([string]$What, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed ($LASTEXITCODE)" }
}

# A fresh folder: files left from an older publish would otherwise go into the package.
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
Invoke-Checked 'Publish' { dotnet publish "$repo\src\Orbit.App" -c Release -o $publish -nologo -v q "-p:Version=$version" }
foreach ($file in 'Orbit.exe', 'THIRD-PARTY-NOTICES.txt', 'engine\TypingEngine.dll', 'engine\NOTICES.txt') {
    if (-not (Test-Path "$publish\$file")) { throw "The publish has no $file" }
}

Push-Location $repo
try {
    Invoke-Checked 'Tool restore' { dotnet tool restore }
    Invoke-Checked 'Pack' { dotnet vpk pack --packId $packId --packVersion $version --packDir $publish --mainExe Orbit.exe --packTitle Orbit --icon "$repo\src\Orbit.App\Assets\Orbit.ico" --outputDir $releases }
} finally {
    Pop-Location
}
Write-Host "Installer: $releases\$packId-win-Setup.exe"
