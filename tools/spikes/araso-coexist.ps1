# M0 spike: Orbit's hooks with Araso typing (docs/plan/03-milestones.md, "Araso coexistence").
# Types real key events (scan codes) into a WPF TextBox (a TSF field) and a WinForms TextBox (the
# classic Edit control, through IMM32's compatibility layer), with Orbit running.
# Key sending and field handling follow Araso's windows/scripts/typing-test.ps1 (MIT).
#
#   powershell -File tools/spikes/araso-coexist.ps1 -Installer <Araso-Setup.exe> -Orbit <Orbit.exe>
#   powershell -File tools/spikes/araso-coexist.ps1 -Orbit <Orbit.exe> -SkipInstall    (test again)
#   powershell -File tools/spikes/araso-coexist.ps1 -Uninstall                         (remove Araso)
#
# Takes over the keyboard and the focus for about two minutes. Install and uninstall ask for UAC.
# Never logs anything but the test strings below.
param(
    [string]$Installer,
    [string]$Orbit,
    [switch]$SkipInstall,
    [switch]$Uninstall,
    [string]$Out = (Join-Path $env:LOCALAPPDATA "Orbit\spike")
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force $Out | Out-Null
$folder = "$env:ProgramFiles\Araso"
. "$PSScriptRoot\input-ids.ps1"
$tip = Get-InputTip araso
$tipKey = "Software\Microsoft\CTF\TIP\{$($InputIds.araso.clsid)}"
$stateFile = Join-Path $Out "araso-before.json"

function Snapshot {
    $run = (Get-ItemProperty "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -ErrorAction SilentlyContinue).Araso
    [ordered]@{
        programFiles = Test-Path $folder
        appData = Test-Path "$env:APPDATA\Araso"
        runValue = [bool]$run
        tipMachine = Test-Path "HKLM:\$tipKey"
        tipMachine32 = Test-Path "HKLM:\Software\WOW6432Node\$($tipKey.Substring(9))"
        tipUser = Test-Path "HKCU:\$tipKey"
        languageTips = @(Get-WinUserLanguageList | ForEach-Object { $_.InputMethodTips }) -join ","
        defaultOverride = [string](Get-WinDefaultInputMethodOverride)
        hostRunning = [bool](Get-Process ArasoHost -ErrorAction SilentlyContinue)
    }
}

if ($Uninstall) {
    $uninstaller = Get-ChildItem $folder -Filter "unins*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($uninstaller) {
        $p = Start-Process $uninstaller.FullName -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -Verb RunAs -PassThru
        $p.WaitForExit()
        Write-Host "uninstaller exit $($p.ExitCode)"
    } else { Write-Host "no uninstaller in $folder" }
    # Araso's unregistration empties its TIP keys but leaves them (beta-1); a silent uninstall
    # keeps %APPDATA%\Araso. Remove both. ArasoTip.dll stays until the next restart while apps hold it.
    $keys = @("HKLM\$tipKey", "HKLM\Software\WOW6432Node\$($tipKey.Substring(9))") | Where-Object { Test-Path ($_ -replace '^HKLM', 'HKLM:') }
    if ($keys) {
        $command = ($keys | ForEach-Object { "reg delete `"$_`" /f" }) -join " & "
        Start-Process cmd -ArgumentList "/c", $command -Verb RunAs -WindowStyle Hidden -Wait
    }
    Remove-Item "$env:APPDATA\Araso" -Recurse -Force -ErrorAction SilentlyContinue
    & "$PSScriptRoot\input-profile.ps1" microsoft
    if (Test-Path $stateFile) {
        $before = Get-Content $stateFile -Raw | ConvertFrom-Json
        if ($before.defaultOverride) { Set-WinDefaultInputMethodOverride -InputTip $before.defaultOverride }
        else { Set-WinDefaultInputMethodOverride }  # no argument clears the override
        Write-Host "before: $($before | ConvertTo-Json -Compress)"
    }
    Start-Sleep -Seconds 2
    Write-Host "after:  $((Snapshot) | ConvertTo-Json -Compress)"
    return
}

. "$PSScriptRoot\spike-common.ps1"
Add-Type @"
using System.Runtime.InteropServices;
public static class ArasoNative {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool WaitNamedPipe(string name, uint timeout);
    [DllImport("input.dll", CharSet = CharSet.Unicode)] public static extern bool InstallLayoutOrTip(string profile, uint flags);
}
"@

# --- Install -------------------------------------------------------------------------------
if (-not $SkipInstall) {
    if (-not (Test-Path $Installer)) { throw "no installer at $Installer" }
    $before = Snapshot
    $before | ConvertTo-Json | Set-Content $stateFile -Encoding utf8
    Write-Host "before: $($before | ConvertTo-Json -Compress)"
    $setup = Start-Process $Installer -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -Verb RunAs -PassThru
    $setup.WaitForExit()
    if ($setup.ExitCode -ne 0) { throw "install failed ($($setup.ExitCode))" }
    Write-Host "installed"
}
if (-not (Test-Path "$folder\ArasoTip.dll")) { throw "Araso is not installed in $folder" }
[ArasoNative]::InstallLayoutOrTip($tip, 0) | Out-Null
# One input method is shared by every app: switch the one in use, and back in the end.
& "$PSScriptRoot\input-profile.ps1" araso

# The host warms up before it opens its pipe; wait for the pipe (as typing-test.ps1 does).
$session = (Get-Process -Id $PID).SessionId
$pipe = "\\.\pipe\Araso.$session.$env:USERNAME"
$clock = [Diagnostics.Stopwatch]::StartNew()
while (-not [ArasoNative]::WaitNamedPipe($pipe, 1)) {
    if ($clock.Elapsed.TotalSeconds -ge 60) { throw "Araso host: no pipe after 60 s" }
    if (-not (Get-Process ArasoHost -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session)) {
        Start-Process "$folder\ArasoHost.exe" -ArgumentList "--server"
    }
    Start-Sleep -Milliseconds 250
}
Write-Host "Araso host ready after $([Math]::Round($clock.Elapsed.TotalSeconds, 1)) s"

Start-Orbit $Orbit

try {
    foreach ($kind in "wpf", "winforms") {
        Case "baseline" $kind "안녕 hello " { Send-Keys "dkssud hello " }
        Case "Ctrl+Alt+Space mid-word (ring)" $kind "안녕한글 " { Send-Keys "dkssud"; CtrlAltSpace; Send-Keys "gksrmf " }
        Case "Ctrl+Alt+Space after a word (ring)" $kind "안녕 한글 " { Send-Keys "dkssud "; CtrlAltSpace; Send-Keys "gksrmf " }
        Case "Alt+Space after a word (launcher)" $kind "안녕 한글 " { Send-Keys "dkssud "; AltSpace; Send-Keys "gksrmf " }
        Case "Alt+Space mid-word (launcher)" $kind "안녕한글 " { Send-Keys "dkssud"; AltSpace; Send-Keys "gksrmf " }
        Case "right Alt tap still switches" $kind "ㅗ디ㅣㅐ " { Send-Keys "hello "; Start-Sleep -Milliseconds 300; Tap 0x38 }
        Case "right Ctrl tap still pauses" $kind "dkssud " { Tap 0x1D; Send-Keys "dkssud "; Tap 0x1D }
        # Snippet expansion as Orbit would do it at the delimiter: wait, delete the word and its
        # Space ("안녕 " is 3 UTF-16 units), type the expansion. The wait Orbit needs is the first one that passes.
        foreach ($wait in 0, 20, 50, 100) {
            $script:expandWait = $wait
            Case "expand after Space, wait $wait ms" $kind "1" { Send-Keys "dkssud "; Start-Sleep -Milliseconds $script:expandWait; Backspaces 3; Send-Keys "1" }
        }
    }
} finally {
    Stop-Orbit
    & "$PSScriptRoot\input-profile.ps1" microsoft
}

$ringOpens = Count-OrbitLog "Test ring opened"
$launcher = Count-OrbitLog "Launcher shortcut pressed"
Write-Host "Orbit saw: ring opened $ringOpens times (expected 4), launcher shortcut $launcher times (expected 4)"
Save-Report $Out "araso-coexist" @{ ringOpens = $ringOpens; launcher = $launcher }
