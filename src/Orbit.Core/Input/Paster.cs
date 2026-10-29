using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Orbit.Core.Input;

public enum PasteResult
{
    Pasted,
    /// <summary>No window to paste into, or it closed.</summary>
    TargetGone,
    /// <summary>The window never came to the front.</summary>
    TargetNotInFront,
    /// <summary>Windows dropped the keys: an elevated window, or the secure desktop.</summary>
    InputBlocked,
}

/// <summary>
/// The last window in front that isn't Orbit's: where a paste from Orbit's panels belongs (Orbit:
/// <c>Paster.lastExternal</c>). Follows EVENT_SYSTEM_FOREGROUND; create it on a thread with a
/// message loop (the UI thread).
/// </summary>
public sealed class ForegroundTracker : IDisposable
{
    private readonly WINEVENTPROC _callback;
    private readonly HWINEVENTHOOK _hook;
    private readonly uint _process = (uint)Environment.ProcessId;

    public ForegroundTracker()
    {
        _callback = OnForeground;
        _hook = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND,
            HMODULE.Null, _callback, 0, 0, PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS);
        Remember(PInvoke.GetForegroundWindow());
    }

    public nint LastExternal { get; private set; }

    public void Dispose()
    {
        if (!_hook.IsNull) PInvoke.UnhookWinEvent(_hook);
    }

    private void OnForeground(HWINEVENTHOOK hook, uint @event, HWND window, int idObject, int idChild, uint thread, uint time) =>
        Remember(window);

    private unsafe void Remember(HWND window)
    {
        if (window.IsNull) return;
        uint process = 0;
        PInvoke.GetWindowThreadProcessId(window, &process);
        if (process != _process) LastExternal = window;
    }
}

/// <summary>
/// Sends Ctrl+V to a window (Orbit: Core/Paster.swift). The window is brought to the front and
/// waited for; held modifiers are let go first, so the app sees Ctrl+V and nothing else. The keys
/// are marked as Orbit's, so its own hook passes them.
/// </summary>
public static class Paster
{
    private const int Attempts = 30;
    private static readonly TimeSpan AttemptDelay = TimeSpan.FromMilliseconds(20);
    /// <summary>A beat for the field to take the keyboard again, as Orbit waits.</summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(40);

    private const uint VkV = 0x56;
    private static readonly uint[] Modifiers =
    [
        HotkeyMatcher.VkLShift, HotkeyMatcher.VkRShift, HotkeyMatcher.VkLControl, HotkeyMatcher.VkRControl,
        HotkeyMatcher.VkLMenu, HotkeyMatcher.VkRMenu, HotkeyMatcher.VkLWin, HotkeyMatcher.VkRWin,
    ];

    public static async Task<PasteResult> PasteAsync(nint target)
    {
        var window = new HWND(target);
        if (window.IsNull || !PInvoke.IsWindow(window)) return PasteResult.TargetGone;

        if (!IsInFront(window)) PInvoke.SetForegroundWindow(window);
        for (int i = 0; i < Attempts && !IsInFront(window); i++) await Task.Delay(AttemptDelay);
        if (!IsInFront(window)) return PasteResult.TargetNotInFront;
        await Task.Delay(SettleDelay);
        // The user may have switched windows during the beat: then no keys at all.
        if (!PInvoke.IsWindow(window)) return PasteResult.TargetGone;
        if (!IsInFront(window)) return PasteResult.TargetNotInFront;
        return SendPaste() ? PasteResult.Pasted : PasteResult.InputBlocked;
    }

    private static bool IsInFront(HWND window)
    {
        var front = PInvoke.GetForegroundWindow();
        return front == window || PInvoke.GetAncestor(front, GET_ANCESTOR_FLAGS.GA_ROOT) == PInvoke.GetAncestor(window, GET_ANCESTOR_FLAGS.GA_ROOT);
    }

    /// <summary>Lets go of held modifiers (masking a held Alt or Win first), then Ctrl+V.</summary>
    private static unsafe bool SendPaste()
    {
        Span<INPUT> inputs = stackalloc INPUT[16];
        int count = 0;
        bool altOrWin = false;
        foreach (var vk in Modifiers)
            if ((PInvoke.GetAsyncKeyState((int)vk) & 0x8000) != 0)
                altOrWin |= vk is HotkeyMatcher.VkLMenu or HotkeyMatcher.VkRMenu or HotkeyMatcher.VkLWin or HotkeyMatcher.VkRWin;
        if (altOrWin)
        {
            inputs[count++] = OrbitInput.Key(HotkeyMatcher.VkMenuMask, up: false);
            inputs[count++] = OrbitInput.Key(HotkeyMatcher.VkMenuMask, up: true);
        }
        foreach (var vk in Modifiers)
            if ((PInvoke.GetAsyncKeyState((int)vk) & 0x8000) != 0) inputs[count++] = OrbitInput.Key(vk, up: true);

        inputs[count++] = OrbitInput.Key(HotkeyMatcher.VkLControl, up: false);
        inputs[count++] = OrbitInput.Key(VkV, up: false);
        inputs[count++] = OrbitInput.Key(VkV, up: true);
        inputs[count++] = OrbitInput.Key(HotkeyMatcher.VkLControl, up: true);
        return PInvoke.SendInput(inputs[..count], sizeof(INPUT)) == count;
    }
}
