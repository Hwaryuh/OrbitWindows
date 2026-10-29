namespace Orbit.Core.Input;

[Flags]
public enum Modifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

/// <summary>A shortcut: modifiers (left keys only) and one virtual key.</summary>
public readonly record struct Hotkey(Modifiers Modifiers, uint Key)
{
    public override string ToString() => $"{Modifiers}+0x{Key:X2}";

    /// <summary>The keys as printed on a keyboard, modifiers in the order Windows writes them (Win, Ctrl, Alt, Shift), then the key.</summary>
    public IReadOnlyList<string> Labels()
    {
        var labels = new List<string>(5);
        if (Modifiers.HasFlag(Modifiers.Win)) labels.Add("Win");
        if (Modifiers.HasFlag(Modifiers.Ctrl)) labels.Add("Ctrl");
        if (Modifiers.HasFlag(Modifiers.Alt)) labels.Add("Alt");
        if (Modifiers.HasFlag(Modifiers.Shift)) labels.Add("Shift");
        labels.Add(KeyName(Key));
        return labels;
    }

    private static string KeyName(uint vk) => vk switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        >= 0x60 and <= 0x69 => $"Num {vk - 0x60}",
        0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x1B => "Esc", 0x20 => "Space",
        0x21 => "PgUp", 0x22 => "PgDn", 0x23 => "End", 0x24 => "Home",
        0x25 => "←", 0x26 => "↑", 0x27 => "→", 0x28 => "↓",
        0x2C => "PrtScn", 0x2D => "Ins", 0x2E => "Del",
        0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-", 0xBE => ".", 0xBF => "/", 0xC0 => "`",
        0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
        _ => $"0x{vk:X2}",
    };
}

/// <summary>Which keys are down right now, by virtual-key code.</summary>
public interface IKeyState
{
    bool IsDown(uint vk);
}

/// <summary>What the keyboard hook does with one key event.</summary>
public readonly record struct HotkeyDecision(bool Swallow, int Pressed, int Released, bool SendMenuMask)
{
    public static readonly HotkeyDecision Pass = new(false, 0, 0, false);
}

/// <summary>
/// Matches shortcuts in the keyboard hook (docs/plan/04-shortcuts.md):
/// <list type="bullet">
/// <item>Required modifiers count only on the left side. Right Alt and right Ctrl down rule any
/// match out: AltGr arrives as left Ctrl + right Alt, and they are Araso's keys. A modifier the
/// shortcut doesn't name rules it out too.</item>
/// <item>A matched key belongs to Orbit until it goes up: its down, its repeats and its up are
/// swallowed, even if the modifiers were let go first. Modifiers are never swallowed.</item>
/// <item>With Alt in the shortcut, the hook sends an unassigned key while Alt is down, so the app
/// never sees a lone Alt tap and opens its menu bar.</item>
/// </list>
/// Ids are the caller's, and must not be 0. Runs on the hook thread; allocation-free.
/// </summary>
public sealed class HotkeyMatcher
{
    public const uint VkSpace = 0x20;
    public const uint VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;
    public const uint VkLWin = 0x5B, VkRWin = 0x5C;
    public const uint VkLShift = 0xA0, VkRShift = 0xA1, VkLControl = 0xA2, VkRControl = 0xA3, VkLMenu = 0xA4, VkRMenu = 0xA5;
    /// <summary>Unassigned (0xE8): what AutoHotkey also sends to mask a lone Alt or Win.</summary>
    public const uint VkMenuMask = 0xE8;

    private readonly int[] _ids = new int[32];
    private readonly Hotkey[] _keys = new Hotkey[32];
    /// <summary>Non-modifier keys down, as the hook saw them: a press the app got is told from its repeats.</summary>
    private readonly bool[] _down = new bool[256];
    private int _count;
    private uint _ownedKey;
    private int _ownedId;

    /// <summary>Set before the hook starts; not while it runs.</summary>
    public void Register(int id, Hotkey hotkey)
    {
        if (id == 0) throw new ArgumentOutOfRangeException(nameof(id), "0 means no hotkey");
        if (_count == _ids.Length) throw new InvalidOperationException("Too many hotkeys");
        _ids[_count] = id;
        _keys[_count] = hotkey;
        _count++;
    }

    public static bool IsModifier(uint vk) => vk is VkShift or VkControl or VkMenu or VkLWin or VkRWin
        or >= VkLShift and <= VkRMenu;

    public HotkeyDecision KeyDown(uint vk, IKeyState state)
    {
        if (IsModifier(vk) || vk >= 256) return HotkeyDecision.Pass;
        bool repeat = _down[vk];
        _down[vk] = true;
        // Auto-repeat of the key Orbit owns: still Orbit's.
        if (vk == _ownedKey) return new HotkeyDecision(true, 0, 0, false);
        // Only a first press can start a shortcut: a key the app already got keeps its repeats and its up.
        if (repeat || _ownedKey != 0) return HotkeyDecision.Pass;

        var held = Held(state);
        if (held == Forbidden) return HotkeyDecision.Pass;
        for (int i = 0; i < _count; i++)
        {
            if (_keys[i].Key != vk || _keys[i].Modifiers != (Modifiers)held) continue;
            _ownedKey = vk;
            _ownedId = _ids[i];
            return new HotkeyDecision(true, _ownedId, 0, (held & (int)(Modifiers.Alt | Modifiers.Win)) != 0);
        }
        return HotkeyDecision.Pass;
    }

    public HotkeyDecision KeyUp(uint vk)
    {
        if (vk < 256) _down[vk] = false;
        if (vk != _ownedKey || _ownedKey == 0) return HotkeyDecision.Pass;
        return new HotkeyDecision(true, 0, ReleaseOwned(), false);
    }

    /// <summary>Forget everything held, as after the hooks were reinstalled. The held shortcut's id, or 0.</summary>
    public int Reset()
    {
        int id = ReleaseOwned();
        Array.Clear(_down);
        return id;
    }

    /// <summary>
    /// Recovers from ups the hook never saw (they went to an elevated window, or the hook was
    /// gone), called now and then. A key the app got is forgotten once Windows says it is up. The
    /// key Orbit owns can't be asked that way (a swallowed key never reaches Windows' key state),
    /// so a shortcut is let go once none of its modifiers is down any more: a ring held by its key
    /// alone, modifiers released, closes at the next check. The released shortcut's id, or 0.
    /// </summary>
    public int Recover(IKeyState state)
    {
        for (uint vk = 0; vk < 256; vk++)
            if (_down[vk] && vk != _ownedKey && !state.IsDown(vk)) _down[vk] = false;

        if (_ownedKey == 0) return 0;
        var shortcut = Modifiers.None;
        for (int i = 0; i < _count; i++)
            if (_ids[i] == _ownedId) shortcut = _keys[i].Modifiers;
        var held = Held(state);
        if (held != Forbidden && (held & (int)shortcut) != 0) return 0;

        _down[_ownedKey] = false;
        return ReleaseOwned();
    }

    /// <summary>The key Orbit owns is the app's again. The shortcut's id, or 0.</summary>
    private int ReleaseOwned()
    {
        int id = _ownedId;
        _ownedKey = 0;
        _ownedId = 0;
        return id;
    }

    private const int Forbidden = -1;

    /// <summary>The left modifiers down, or <see cref="Forbidden"/> if a right one is.</summary>
    private static int Held(IKeyState state)
    {
        if (state.IsDown(VkRMenu) || state.IsDown(VkRControl) || state.IsDown(VkRShift) || state.IsDown(VkRWin))
            return Forbidden;
        int held = 0;
        if (state.IsDown(VkLControl)) held |= (int)Modifiers.Ctrl;
        if (state.IsDown(VkLMenu)) held |= (int)Modifiers.Alt;
        if (state.IsDown(VkLShift)) held |= (int)Modifiers.Shift;
        if (state.IsDown(VkLWin)) held |= (int)Modifiers.Win;
        return held;
    }
}
