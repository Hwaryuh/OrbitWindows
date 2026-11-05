# Development only: replaces the registered ArasoTip.dll with a new build, in place. Run elevated.
# A DLL in use can't be overwritten but can be renamed, so apps already open keep the old one and
# apps started afterwards load the new one. Nothing is registered again and the host isn't stopped.
#   powershell -File tools/araso/deploy-tip.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot\..\..").Path
$folder = (Get-ItemProperty 'HKLM:\SOFTWARE\Orbit\Araso').Folder
if (-not (Test-Path "$folder\ArasoTip.dll")) { throw "No installed text service in $folder" }
$stamp = Get-Date -Format 'yyyyMMddHHmmss'
foreach ($part in @(
        @{ From = "$repo\artifacts\araso-stage\ArasoTip.dll"; To = "$folder\ArasoTip.dll" },
        @{ From = "$repo\artifacts\araso-stage\x86\ArasoTip.dll"; To = "$folder\x86\ArasoTip.dll" })) {
    Move-Item $part.To "$($part.To).$stamp.old"
    Copy-Item $part.From $part.To
    Write-Host "Replaced $($part.To)"
}
