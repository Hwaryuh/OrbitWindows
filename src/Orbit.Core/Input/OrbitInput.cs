using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Orbit.Core.Input;

/// <summary>Key events Orbit sends itself, marked so its own hook lets them through.</summary>
public static class OrbitInput
{
    public static INPUT Key(uint vk, bool up)
    {
        var flags = up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0;
        if (vk is HotkeyMatcher.VkRControl or HotkeyMatcher.VkRMenu or HotkeyMatcher.VkLWin or HotkeyMatcher.VkRWin)
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
        return Event((ushort)vk, (ushort)PInvoke.MapVirtualKey(vk, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC), flags, HookThread.OrbitMarker);
    }

    /// <summary>Down in Windows' view, right now.</summary>
    public static bool IsDown(uint vk) => (PInvoke.GetAsyncKeyState((int)vk) & 0x8000) != 0;

    /// <summary>One keyboard event, as given.</summary>
    public static INPUT Event(ushort vk, ushort scan, KEYBD_EVENT_FLAGS flags, nuint marker)
    {
        var input = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        input.Anonymous.ki = new KEYBDINPUT { wVk = (VIRTUAL_KEY)vk, wScan = scan, dwFlags = flags, dwExtraInfo = marker };
        return input;
    }
}
