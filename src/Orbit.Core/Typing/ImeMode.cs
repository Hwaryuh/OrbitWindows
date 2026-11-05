using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Orbit.Core.Typing;

/// <summary>
/// Holds the Microsoft IME in alphanumeric mode, so letters Orbit sends aren't composed a second time.
/// A window that stays there after a crash is one 한/영 press from normal, which is why no record of
/// old modes is kept beyond the last window.
/// </summary>
public sealed class ImeMode
{
    private const int LimitMs = 50;
    private const uint WmImeControl = 0x0283;
    private const nuint GetConversionMode = 1, SetConversionMode = 2;
    private const nuint Native = 1;
    private const uint PrimaryLanguageMask = 0x3FF, Korean = 0x12;

    private HWND _changed;
    private nuint _modeBefore;

    /// <summary>The window with the keyboard's focus, or the one in front.</summary>
    public static unsafe HWND FocusWindow(out bool hasCaret)
    {
        var front = PInvoke.GetForegroundWindow();
        uint thread = PInvoke.GetWindowThreadProcessId(front, null);
        var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
        bool known = PInvoke.GetGUIThreadInfo(thread, ref info);
        hasCaret = known && !info.hwndCaret.IsNull;
        return known && !info.hwndFocus.IsNull ? info.hwndFocus : front;
    }

    /// <summary>The language of the keyboard layout in use in that window.</summary>
    public static unsafe ushort Language(HWND window)
    {
        uint thread = PInvoke.GetWindowThreadProcessId(window, null);
        return (ushort)(nuint)PInvoke.GetKeyboardLayout(thread).Value;
    }

    /// <summary>True when letters go out as letters: not a Korean layout, or the IME agreed.</summary>
    public bool Hold()
    {
        var window = FocusWindow(out _);
        if (window.IsNull) return false;
        if ((Language(window) & PrimaryLanguageMask) != Korean) return true;
        var ime = PInvoke.ImmGetDefaultIMEWnd(window);
        if (ime.IsNull) return false;
        if (Ask(ime, GetConversionMode, 0) is not { } mode) return false;
        if ((mode & Native) == 0) return true;
        Ask(ime, SetConversionMode, mode & ~Native);
        if (Ask(ime, GetConversionMode, 0) is not { } after || (after & Native) != 0)
        {
            Log.Info("The Microsoft IME stayed in Hangul mode");
            return false;
        }
        Log.Info("The Microsoft IME went to alphanumeric mode");
        _changed = window;
        _modeBefore = mode;
        return true;
    }

    /// <summary>The last window changed gets its mode back, if it is still the one with focus.</summary>
    public void Release()
    {
        var window = _changed;
        _changed = default;
        if (window.IsNull || !PInvoke.IsWindow(window) || FocusWindow(out _) != window) return;
        var ime = PInvoke.ImmGetDefaultIMEWnd(window);
        if (!ime.IsNull) Ask(ime, SetConversionMode, _modeBefore);
    }

    private static unsafe nuint? Ask(HWND ime, nuint command, nuint value)
    {
        nuint result = 0;
        var answered = PInvoke.SendMessageTimeout(ime, WmImeControl, command, (nint)value,
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG, LimitMs, &result);
        return answered.Value != 0 ? result : null;
    }
}
