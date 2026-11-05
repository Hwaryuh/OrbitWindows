# Orbit's hotkey matcher with real key events : shortcuts swallowed, no menu bar, keys
# the app already got left alone. Digits only, so the IME's Korean or English mode doesn't matter
# (full-width mode would show as ０-９).
#   powershell -File tools/spikes/hotkey-check.ps1 -Orbit <Orbit.exe>
# Takes over the keyboard and the focus for about 30 seconds.
param(
    [Parameter(Mandatory)][string]$Orbit,
    [string]$Out = (Join-Path $env:LOCALAPPDATA "Orbit\spike")
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force $Out | Out-Null
. "$PSScriptRoot\spike-common.ps1"

Start-Orbit $Orbit
try {
    foreach ($kind in "wpf", "winforms") {
        Case "baseline" $kind "12 34" { Send-Keys "12 34" }
        Case "Ctrl+Alt+Space is swallowed (ring)" $kind "1234" { Send-Keys "12"; CtrlAltSpace; Send-Keys "34" }
        Case "Alt+Space: no menu bar (launcher)" $kind "1234" { Send-Keys "12"; AltSpace; Send-Keys "34" }
        Case "Alt+Space with Alt let go at once" $kind "1234" {
            Send-Keys "12"
            [Keys]::Key(0x38, $false, $false); [Keys]::Key(0x39, $false, $false)
            [Keys]::Key(0x39, $true, $false); [Keys]::Key(0x38, $true, $false)
            Start-Sleep -Milliseconds 150
            Send-Keys "34"
        }
        # Space goes to the app, then Ctrl+Alt while it repeats: the repeats and the up stay the app's
        # (the ring must not open). A text box types no space for Ctrl+Alt+Space, so one space.
        Case "Space already down stays the app's" $kind " 1" {
            Down 0x39; Down 0x1D; Down 0x38; Down 0x39; Down 0x39; Up 0x39; Up 0x38; Up 0x1D
            Start-Sleep -Milliseconds 150
            Send-Keys "1"
        }
    }
} finally {
    Stop-Orbit
}

$ringOpens = Count-OrbitLog "Test ring opened"
$launcher = Count-OrbitLog "Launcher shortcut pressed"
Write-Host "Orbit saw: ring opened $ringOpens times (expected 2), launcher shortcut $launcher times (expected 4)"
Save-Report $Out "hotkey-check" @{ ringOpens = $ringOpens; launcher = $launcher }
