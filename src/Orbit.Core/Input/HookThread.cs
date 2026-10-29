using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Orbit.Core.Input;

public enum HookNotice { RingOpen, RingRelease, HooksReinstalled }

/// <summary>
/// The low-level keyboard and mouse hooks, on a thread of their own with its own message loop, so
/// WPF layout and rendering never delay them. The hook procedures don't allocate (a GC still
/// suspends this thread like any other) and stay far below LowLevelHooksTimeout; anything slower is
/// posted to this thread's loop. <see cref="Notice"/> is raised on this thread, outside the hook
/// procedures: subscribers marshal to their own thread.
/// </summary>
public sealed class HookThread : IDisposable
{
    /// <summary>dwExtraInfo of events Orbit sends itself (Orbit's replayMarker 0x0B17).</summary>
    public const nuint OrbitMarker = 0x0B17;

    private const uint WmReplay = PInvoke.WM_APP + 1;
    private const uint WmNotify = PInvoke.WM_APP + 2;
    private const uint WatchdogMs = 2000;

    private readonly HOOKPROC _mouseProc;
    private readonly HOOKPROC _keyboardProc;
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private uint _threadId;
    private HHOOK _mouseHook;
    private HHOOK _keyboardHook;
    private nuint _holdTimer;
    private nuint _watchdogTimer;
    private uint _lastMouseTick;
    private uint _lastKeyboardTick;
    private System.Drawing.Point _lastCursor;
    private uint _lastReinstall;
    private uint _backoffUntil;
    private uint _lastReinstallLog;

    public HookThread()
    {
        // Kept in fields: a delegate only the native side holds would be collected.
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
    }

    /// <summary>The ring's mouse trigger. Touched only on the hook thread, except its settings.</summary>
    public MouseHoldTrigger RingTrigger { get; } = new();

    /// <summary>Raised on the hook thread with the press position in physical pixels.</summary>
    public event Action<HookNotice, int, int>? Notice;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Run) { Name = "Orbit hooks", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _ready.Wait();
    }

    public void Dispose()
    {
        if (_thread is null) return;
        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
        _ready.Dispose();
    }

    private void Run()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        // Creates this thread's message queue before anyone posts to it.
        PInvoke.PeekMessage(out _, HWND.Null, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);
        Install();
        PInvoke.GetCursorPos(out _lastCursor);
        _watchdogTimer = PInvoke.SetTimer(HWND.Null, 0, WatchdogMs, null);
        _ready.Set();
        Log.Info("Hooks installed");

        while (true)
        {
            int result = PInvoke.GetMessage(out var msg, HWND.Null, 0, 0).Value;
            if (result == 0) break;
            if (result == -1)
            {
                Log.Error("GetMessage failed on the hook thread");
                break;
            }
            Handle(in msg);
        }

        PInvoke.KillTimer(HWND.Null, _watchdogTimer);
        Uninstall();
        Log.Info("Hooks removed");
    }

    private void Handle(in MSG msg)
    {
        switch (msg.message)
        {
            case PInvoke.WM_TIMER when msg.wParam.Value == _holdTimer:
                StopHoldTimer();
                if ((RingTrigger.TimerElapsed() & TriggerAction.Open) != 0)
                    Raise(HookNotice.RingOpen, RingTrigger.PressX, RingTrigger.PressY);
                break;
            case PInvoke.WM_TIMER when msg.wParam.Value == _watchdogTimer:
                CheckHooks();
                break;
            case WmReplay:
                ReplayClick((MouseButton)(int)msg.wParam.Value);
                break;
            case WmNotify:
                Raise((HookNotice)(int)msg.wParam.Value, 0, 0);
                break;
            default:
                PInvoke.TranslateMessage(in msg);
                PInvoke.DispatchMessage(in msg);
                break;
        }
    }

    private void Raise(HookNotice notice, int x, int y)
    {
        try
        {
            Notice?.Invoke(notice, x, y);
        }
        catch (Exception e)
        {
            Log.Error($"Hook notice {notice} failed", e);
        }
    }

    private unsafe LRESULT MouseProc(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0)
        {
            _lastMouseTick = PInvoke.GetTickCount();
            var info = (MSLLHOOKSTRUCT*)lParam.Value;
            if (info->dwExtraInfo != OrbitMarker)
            {
                var (button, down) = Decode((uint)wParam.Value, info->mouseData);
                if (button != MouseButton.None)
                {
                    var action = down ? RingTrigger.Down(button, info->pt.X, info->pt.Y) : RingTrigger.Up(button);
                    if (action != TriggerAction.None)
                    {
                        Apply(action, button);
                        if ((action & TriggerAction.Swallow) != 0) return (LRESULT)1;
                    }
                }
            }
        }
        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    private LRESULT KeyboardProc(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0) _lastKeyboardTick = PInvoke.GetTickCount();
        // The hotkey matcher comes here (docs/plan/04-shortcuts.md). Until then every key passes.
        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    /// <summary>Inside the hook procedure: only timers and posted messages, nothing that blocks.</summary>
    private void Apply(TriggerAction action, MouseButton button)
    {
        if ((action & TriggerAction.StopTimer) != 0) StopHoldTimer();
        if ((action & TriggerAction.StartTimer) != 0)
            _holdTimer = PInvoke.SetTimer(HWND.Null, 0, (uint)MouseHoldTrigger.HoldDelay.TotalMilliseconds, null);
        if ((action & TriggerAction.ReplayClick) != 0)
            PInvoke.PostThreadMessage(_threadId, WmReplay, (nuint)(int)button, default);
        if ((action & TriggerAction.Release) != 0)
            PInvoke.PostThreadMessage(_threadId, WmNotify, (nuint)(int)HookNotice.RingRelease, default);
    }

    private void StopHoldTimer()
    {
        if (_holdTimer == 0) return;
        PInvoke.KillTimer(HWND.Null, _holdTimer);
        _holdTimer = 0;
    }

    private static (MouseButton Button, bool Down) Decode(uint message, uint mouseData) => message switch
    {
        PInvoke.WM_LBUTTONDOWN => (MouseButton.Left, true),
        PInvoke.WM_LBUTTONUP => (MouseButton.Left, false),
        PInvoke.WM_RBUTTONDOWN => (MouseButton.Right, true),
        PInvoke.WM_RBUTTONUP => (MouseButton.Right, false),
        PInvoke.WM_MBUTTONDOWN => (MouseButton.Middle, true),
        PInvoke.WM_MBUTTONUP => (MouseButton.Middle, false),
        PInvoke.WM_XBUTTONDOWN => (XButton(mouseData), true),
        PInvoke.WM_XBUTTONUP => (XButton(mouseData), false),
        _ => (MouseButton.None, false),
    };

    private static MouseButton XButton(uint mouseData) => (mouseData >> 16) == 1 ? MouseButton.X1 : MouseButton.X2;

    private static unsafe void ReplayClick(MouseButton button)
    {
        var (downFlag, upFlag, data) = button switch
        {
            MouseButton.Left => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP, 0u),
            MouseButton.Right => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP, 0u),
            MouseButton.Middle => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP, 0u),
            MouseButton.X1 => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP, 1u),
            MouseButton.X2 => (MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN, MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP, 2u),
            _ => (default(MOUSE_EVENT_FLAGS), default(MOUSE_EVENT_FLAGS), 0u),
        };
        if (downFlag == default) return;

        Span<INPUT> inputs = stackalloc INPUT[2];
        inputs[0].type = INPUT_TYPE.INPUT_MOUSE;
        inputs[0].Anonymous.mi = new MOUSEINPUT { dwFlags = downFlag, mouseData = data, dwExtraInfo = OrbitMarker };
        inputs[1].type = INPUT_TYPE.INPUT_MOUSE;
        inputs[1].Anonymous.mi = new MOUSEINPUT { dwFlags = upFlag, mouseData = data, dwExtraInfo = OrbitMarker };
        PInvoke.SendInput(inputs, sizeof(INPUT));
    }

    private void Install()
    {
        var module = PInvoke.GetModuleHandle((string?)null);
        _mouseHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _mouseProc, (HINSTANCE)module.DangerousGetHandle(), 0);
        _keyboardHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _keyboardProc, (HINSTANCE)module.DangerousGetHandle(), 0);
        if (_mouseHook.IsNull || _keyboardHook.IsNull)
            Log.Error($"SetWindowsHookEx failed (mouse {!_mouseHook.IsNull}, keyboard {!_keyboardHook.IsNull})");
        _lastMouseTick = _lastKeyboardTick = PInvoke.GetTickCount();
    }

    private void Uninstall()
    {
        StopHoldTimer();
        if (!_mouseHook.IsNull) PInvoke.UnhookWindowsHookEx(_mouseHook);
        if (!_keyboardHook.IsNull) PInvoke.UnhookWindowsHookEx(_keyboardHook);
        _mouseHook = default;
        _keyboardHook = default;
    }

    /// <summary>
    /// Windows removes a hook that times out too often, and says nothing. Two signs of that:
    /// input arrived that neither hook saw (GetLastInputInfo can't tell keys from the mouse), or
    /// the pointer moved without one mouse event. Then put both back and forget any press in
    /// progress. Input to an elevated window never reaches these hooks either, and SetCursorPos
    /// moves the pointer without an event, so a reinstall that didn't help backs off for a while.
    /// </summary>
    private unsafe void CheckHooks()
    {
        uint now = PInvoke.GetTickCount();
        PInvoke.GetCursorPos(out var cursor);
        bool pointerMoved = cursor.X != _lastCursor.X || cursor.Y != _lastCursor.Y;
        bool mouseSilent = (int)(now - _lastMouseTick) > (int)WatchdogMs;
        _lastCursor = cursor;

        var last = new LASTINPUTINFO { cbSize = (uint)sizeof(LASTINPUTINFO) };
        bool unseenInput = PInvoke.GetLastInputInfo(ref last)
            && now - last.dwTime < 5000
            && (int)(last.dwTime - Math.Max(_lastMouseTick, _lastKeyboardTick)) > 1000;
        bool mouseLost = pointerMoved && mouseSilent;
        if (!unseenInput && !mouseLost) return;
        if (_backoffUntil != 0 && (int)(now - _backoffUntil) < 0) return;

        // A second reinstall within 10 s means it isn't helping: probably an elevated window.
        _backoffUntil = (int)(now - _lastReinstall) < 10_000 ? now + 30_000 : 0;
        _lastReinstall = now;

        Uninstall();
        Install();
        RingTrigger.Reset();
        if ((int)(now - _lastReinstallLog) > 60_000)
        {
            _lastReinstallLog = now;
            Log.Warn($"Hooks reinstalled (unseen input {unseenInput}, pointer moved without mouse events {mouseLost})");
        }
        Raise(HookNotice.HooksReinstalled, 0, 0);
    }
}
