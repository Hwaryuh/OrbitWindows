# What the typing spikes share: real key events (scan codes, as a keyboard sends them), test
# fields, cases, and Orbit started with its log watched.
# (MIT). Dot-source it: . "$PSScriptRoot\spike-common.ps1"
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Keys {
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr process);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    const uint SCANCODE = 0x8, KEYUP = 0x2, EXTENDED = 0x1;
    public static void Key(ushort scan, bool up, bool extended) {
        var input = new INPUT[1];
        input[0].type = 1;
        input[0].ki.wScan = scan;
        input[0].ki.dwFlags = SCANCODE | (up ? KEYUP : 0) | (extended ? EXTENDED : 0);
        SendInput(1, input, Marshal.SizeOf(typeof(INPUT)));
    }
    /// Brings a window to the front even when the foreground lock would refuse.
    public static bool Front(IntPtr window) {
        IntPtr front = GetForegroundWindow();
        uint thread = front == IntPtr.Zero ? 0 : GetWindowThreadProcessId(front, IntPtr.Zero);
        uint own = GetCurrentThreadId();
        if (thread != 0 && thread != own) AttachThreadInput(own, thread, true);
        bool done = SetForegroundWindow(window);
        if (thread != 0 && thread != own) AttachThreadInput(own, thread, false);
        return done;
    }
}
"@

# --- Keys ----------------------------------------------------------------------------------
$script:Scans = @{ " " = 0x39 }
foreach ($row in @(@("1234567890", 0x02), @("qwertyuiop", 0x10), @("asdfghjkl", 0x1E), @("zxcvbnm", 0x2C))) {
    for ($i = 0; $i -lt $row[0].Length; $i++) { $script:Scans[[string]$row[0][$i]] = $row[1] + $i }
}
function Send-Keys([string]$keys) {
    foreach ($ch in $keys.ToCharArray()) {
        $scan = $script:Scans[[string]$ch]
        [Keys]::Key($scan, $false, $false); Start-Sleep -Milliseconds 40
        [Keys]::Key($scan, $true, $false); Start-Sleep -Milliseconds 40
    }
}
function Down([int]$scan, [bool]$ext = $false) { [Keys]::Key($scan, $false, $ext); Start-Sleep -Milliseconds 50 }
function Up([int]$scan, [bool]$ext = $false) { [Keys]::Key($scan, $true, $ext); Start-Sleep -Milliseconds 50 }
function Tap([int]$scan) { Down $scan $true; Up $scan $true; Start-Sleep -Milliseconds 150 }
# Left Ctrl 0x1D, left Alt 0x38, Space 0x39; right Alt and right Ctrl are the same scans, extended.
function CtrlAltSpace { Down 0x1D; Down 0x38; Down 0x39; Up 0x39; Up 0x38; Up 0x1D; Start-Sleep -Milliseconds 150 }
function AltSpace { Down 0x38; Down 0x39; Up 0x39; Up 0x38; Start-Sleep -Milliseconds 150 }
function Backspaces([int]$n) { for ($i = 0; $i -lt $n; $i++) { Down 0x0E; Up 0x0E } }

# --- Fields --------------------------------------------------------------------------------
# Not Notepad: Windows 11's Notepad restores killed tabs, which would keep a test's text.
function Open-Field([string]$kind) {
    $file = Join-Path $env:TEMP "orbit-spike-field.txt"
    Remove-Item $file -ErrorAction SilentlyContinue
    $process = Start-Process powershell -ArgumentList "-STA", "-NoProfile", "-File", "$PSScriptRoot\field.ps1", $kind, $file -PassThru
    for ($i = 0; $i -lt 60 -and $process.MainWindowHandle -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 250; $process.Refresh() }
    Start-Sleep -Seconds 2
    return @{ process = $process; window = $process.MainWindowHandle; read = { if (Test-Path $file) { [IO.File]::ReadAllText($file) } else { "" } }.GetNewClosure() }
}

# In front, in Korean (the language the Microsoft IME is listed under).
function Ready($field) {
    for ($i = 0; $i -lt 10 -and [Keys]::GetForegroundWindow() -ne $field.window; $i++) { [Keys]::Front($field.window) | Out-Null; Start-Sleep -Milliseconds 300 }
    $thread = [Keys]::GetWindowThreadProcessId($field.window, [IntPtr]::Zero)
    for ($i = 0; $i -lt 10 -and -not ("{0:X8}" -f [Keys]::GetKeyboardLayout($thread).ToInt64()).EndsWith("0412"); $i++) {
        [Keys]::PostMessage($field.window, 0x50, [IntPtr]::Zero, [IntPtr]0x04120412) | Out-Null
        Start-Sleep -Milliseconds 800
    }
}

$script:SpikeResults = [System.Collections.Generic.List[object]]::new()
function Case([string]$name, [string]$kind, [string]$expected, [scriptblock]$keys) {
    $field = Open-Field $kind
    Ready $field
    & $keys
    Start-Sleep -Milliseconds 800
    $text = & $field.read
    $front = [Keys]::GetForegroundWindow() -eq $field.window
    Stop-Process -Id $field.process.Id -Force -ErrorAction SilentlyContinue
    $ok = $text -eq $expected
    $script:SpikeResults.Add([pscustomobject]@{ case = $name; field = $kind; expected = $expected; got = $text; inFront = $front; ok = $ok })
    Write-Host ("{0,-4} {1,-8} {2,-44} '{3}'" -f $(if ($ok) { "PASS" } else { "FAIL" }), $kind, $name, $text)
}

# --- Orbit ---------------------------------------------------------------------------------
function Start-Orbit([string]$path) {
    Get-Process Orbit -ErrorAction SilentlyContinue | Stop-Process -Force
    $script:OrbitLog = Join-Path $env:LOCALAPPDATA "Orbit\logs\orbit-$(Get-Date -Format yyyyMMdd).log"
    $script:OrbitLogStart = if (Test-Path $script:OrbitLog) { (Get-Content $script:OrbitLog).Count } else { 0 }
    $script:OrbitProcess = Start-Process $path -PassThru
    Start-Sleep -Seconds 3
}
function Stop-Orbit { Stop-Process -Id $script:OrbitProcess.Id -Force -ErrorAction SilentlyContinue }
# How many lines Orbit logged since Start-Orbit that match a pattern.
function Count-OrbitLog([string]$pattern) {
    if (-not (Test-Path $script:OrbitLog)) { return 0 }
    @(Get-Content $script:OrbitLog | Select-Object -Skip $script:OrbitLogStart | Select-String $pattern).Count
}

function Save-Report([string]$folder, [string]$name, [hashtable]$extra = @{}) {
    $report = Join-Path $folder "$name-$(Get-Date -Format yyyyMMdd-HHmmss).json"
    ([ordered]@{ results = $script:SpikeResults } + $extra) | ConvertTo-Json -Depth 4 | Set-Content $report -Encoding utf8
    Write-Host "report: $report"
}
