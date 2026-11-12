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
    private nuint _modeHeld;
    // The window, with its layout, whose IME last answered that letters go out as letters.
    private HWND _sure;
    private ushort _sureLanguage;

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

    /// <summary>
    /// True when letters go out as letters: not a Korean layout, or the IME agreed. An IME that doesn't
    /// answer in time, as a busy app's doesn't, leaves the last answer for the same window and layout
    /// standing: asked again on every word, one late reply would stop typing until focus moved.
    /// </summary>
    public bool Hold()
    {
        var window = FocusWindow(out _);
        if (window.IsNull) return false;
        // Only one window is remembered, so the one before gets its mode back first.
        if (window != _changed) GiveBack();
        ushort language = Language(window);
        if (HoldIn(window, language) is not { } held) return window == _sure && language == _sureLanguage;
        _sure = held ? window : default;
        _sureLanguage = language;
        return held;
    }

    /// <summary>
    /// Keys are no longer Orbit's: 한/영 now reaches the app, so no earlier answer can stand in for a late
    /// one when they come back.
    /// </summary>
    public void Release()
    {
        _sure = default;
        GiveBack();
    }

    /// <summary>Null: the IME didn't answer before anything was seen of its mode.</summary>
    private bool? HoldIn(HWND window, ushort language)
    {
        if ((language & PrimaryLanguageMask) != Korean) return true;
        var ime = PInvoke.ImmGetDefaultIMEWnd(window);
        if (ime.IsNull) return false;
        if (Ask(ime, GetConversionMode, 0) is not { } mode) return null;
        if ((mode & Native) == 0) return true;
        nuint held = mode & ~Native;
        Ask(ime, SetConversionMode, held);
        if (Ask(ime, GetConversionMode, 0) is not { } after || (after & Native) != 0)
        {
            Log.Info("The Microsoft IME stayed in Hangul mode");
            return false;
        }
        Log.Info("The Microsoft IME went to alphanumeric mode");
        _changed = window;
        _modeBefore = mode;
        _modeHeld = held;
        return true;
    }

    /// <summary>
    /// The last window changed gets its mode back, wherever focus is now: a pause usually comes after focus
    /// moved. Each window has a mode of its own, and one left in alphanumeric stayed so after Orbit moved on,
    /// which Minecraft's 한/영 key couldn't undo (2026-11-06).
    /// </summary>
    private void GiveBack()
    {
        var window = _changed;
        _changed = default;
        if (window.IsNull || !PInvoke.IsWindow(window)) return;
        if (window == _sure) _sure = default;
        var ime = PInvoke.ImmGetDefaultIMEWnd(window);
        // A mode other than the one held is the user's own choice since: it stays.
        if (!ime.IsNull && Ask(ime, GetConversionMode, 0) == _modeHeld) Ask(ime, SetConversionMode, _modeBefore);
    }

    private static unsafe nuint? Ask(HWND ime, nuint command, nuint value)
    {
        nuint result = 0;
        var answered = PInvoke.SendMessageTimeout(ime, WmImeControl, command, (nint)value,
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG, LimitMs, &result);
        return answered.Value != 0 ? result : null;
    }
}
