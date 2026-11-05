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
/// Ids are the caller's, must not be 0 and stay below <see cref="RecordedFlag"/>. The hook side runs
/// on the hook thread and doesn't allocate. The shortcuts and what the app in front keeps sit in one
/// table that is replaced whole, so the hook never sees one without the other, and a key held across
/// a replacement stays owned with the modifiers it was matched with.
/// </summary>
public sealed class HotkeyMatcher
{
    public const uint VkSpace = 0x20;
    public const uint VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;
    public const uint VkLWin = 0x5B, VkRWin = 0x5C;
    public const uint VkLShift = 0xA0, VkRShift = 0xA1, VkLControl = 0xA2, VkRControl = 0xA3, VkLMenu = 0xA4, VkRMenu = 0xA5;
    /// <summary>Unassigned (0xE8): what AutoHotkey also sends to mask a lone Alt or Win.</summary>
    public const uint VkMenuMask = 0xE8;

    /// <summary>How many shortcuts a table holds: what the app in front keeps is one bit each.</summary>
    public const int MaxShortcuts = 64;

    /// <summary>
    /// Marks an id as a recorded key rather than a shortcut (see <see cref="Recording"/>): the
    /// modifiers from bit 16, the key below.
    /// </summary>
    public const int RecordedFlag = 1 << 30;

    private sealed class Table(int[] ids, Hotkey[] keys, long pass)
    {
        public readonly int[] Ids = ids;
        public readonly Hotkey[] Keys = keys;
        /// <summary>Shortcuts that go to the app in front instead, one bit each. Changed through <see cref="UpdatePassThrough"/>.</summary>
        public long Pass = pass;
    }

    private volatile Table _table = new([], [], 0);
    /// <summary>Non-modifier keys down, as the hook saw them: a press the app got is told from its repeats.</summary>
    private readonly bool[] _down = new bool[256];
    private uint _ownedKey;
    private int _ownedId;
    private Modifiers _ownedModifiers;
    /// <summary>A bare recorded key let go by <see cref="Recover"/> while perhaps still held: its repeats and its up stay Orbit's.</summary>
    private uint _recoveredKey;
    private volatile bool _recording;

    /// <summary>
    /// While set, no shortcut matches: the next key pressed with left modifiers belongs to Orbit and
    /// comes back as a press with <see cref="Recorded"/> in its id. The menu-mask key goes out as for
    /// a shortcut, so recording Alt or Win combinations opens no menu bar or Start menu. Whoever
    /// sets this must clear it on every way out, or every shortcut stays dead.
    /// </summary>
    public bool Recording { set => _recording = value; }

    /// <summary>The key a press carries when it came from <see cref="Recording"/>; null for a shortcut's id.</summary>
    public static Hotkey? Recorded(int id) =>
        (id & RecordedFlag) == 0 ? null : new Hotkey((Modifiers)((id >> 16) & 0xF), (uint)(id & 0xFFFF));

    /// <summary>
    /// Puts these shortcuts in place at once, from any thread at any time. <paramref name="passes"/>
    /// picks the ones the app in front keeps. A key held meanwhile stays owned until it goes up.
    /// </summary>
    public void Replace(IReadOnlyList<(int Id, Hotkey Key)> shortcuts, Func<Hotkey, bool>? passes = null)
    {
        if (shortcuts.Count > MaxShortcuts) throw new ArgumentException($"More than {MaxShortcuts} shortcuts", nameof(shortcuts));
        var ids = new int[shortcuts.Count];
        var keys = new Hotkey[shortcuts.Count];
        for (int i = 0; i < shortcuts.Count; i++)
        {
            if (shortcuts[i].Id is <= 0 or >= RecordedFlag) throw new ArgumentOutOfRangeException(nameof(shortcuts), "An id is 1 or more and below RecordedFlag");
            ids[i] = shortcuts[i].Id;
            keys[i] = shortcuts[i].Key;
        }
        _table = new Table(ids, keys, passes is null ? 0 : PassMask(keys, passes));
    }

    /// <summary>Adds one shortcut to the table in place.</summary>
    public void Register(int id, Hotkey hotkey)
    {
        if (id == 0) throw new ArgumentOutOfRangeException(nameof(id), "0 means no hotkey");
        var table = _table;
        var shortcuts = new List<(int, Hotkey)>(table.Ids.Length + 1);
        for (int i = 0; i < table.Ids.Length; i++) shortcuts.Add((table.Ids[i], table.Keys[i]));
        shortcuts.Add((id, hotkey));
        Replace(shortcuts);
    }

    /// <summary>
    /// The shortcuts that go to the app in front, worked out when it came to the front (from any
    /// thread): the hook only reads the result, never asks which app it is.
    /// </summary>
    public void UpdatePassThrough(Func<Hotkey, bool> passes)
    {
        var table = _table;
        Volatile.Write(ref table.Pass, PassMask(table.Keys, passes));
    }

    private static long PassMask(Hotkey[] keys, Func<Hotkey, bool> passes)
    {
        long mask = 0;
        for (int i = 0; i < keys.Length; i++)
            if (passes(keys[i])) mask |= 1L << i;
        return mask;
    }

    public static bool IsModifier(uint vk) => vk is VkShift or VkControl or VkMenu or VkLWin or VkRWin
        or >= VkLShift and <= VkRMenu;

    public HotkeyDecision KeyDown(uint vk, IKeyState state)
    {
        if (IsModifier(vk) || vk >= 256) return HotkeyDecision.Pass;
        if (vk == _recoveredKey) return new HotkeyDecision(true, 0, 0, false);
        bool repeat = _down[vk];
        _down[vk] = true;
        // Auto-repeat of the key Orbit owns: still Orbit's.
        if (vk == _ownedKey) return new HotkeyDecision(true, 0, 0, false);
        // Only a first press can start a shortcut: a key the app already got keeps its repeats and its up.
        if (repeat || _ownedKey != 0) return HotkeyDecision.Pass;

        var held = Held(state);
        if (held == Forbidden) return HotkeyDecision.Pass;
        if (_recording) return Own(vk, RecordedFlag | (held << 16) | (int)vk, held);
        var table = _table;
        long pass = Volatile.Read(ref table.Pass);
        for (int i = 0; i < table.Ids.Length; i++)
        {
            if (table.Keys[i].Key != vk || table.Keys[i].Modifiers != (Modifiers)held) continue;
            // The app in front keeps this one: the key goes on to it untouched.
            if ((pass & (1L << i)) != 0) return HotkeyDecision.Pass;
            return Own(vk, table.Ids[i], held);
        }
        return HotkeyDecision.Pass;
    }

    private HotkeyDecision Own(uint vk, int id, int held)
    {
        _ownedKey = vk;
        _ownedId = id;
        _ownedModifiers = (Modifiers)held;
        return new HotkeyDecision(true, id, 0, (held & (int)(Modifiers.Alt | Modifiers.Win)) != 0);
    }

    public HotkeyDecision KeyUp(uint vk)
    {
        if (vk < 256) _down[vk] = false;
        if (vk == _recoveredKey && vk != 0)
        {
            _recoveredKey = 0;
            return new HotkeyDecision(true, 0, 0, false);
        }
        if (vk != _ownedKey || _ownedKey == 0) return HotkeyDecision.Pass;
        return new HotkeyDecision(true, 0, ReleaseOwned(), false);
    }

    /// <summary>Forget everything held, as after the hooks were reinstalled. The held shortcut's id, or 0.</summary>
    public int Reset()
    {
        int id = ReleaseOwned();
        _recoveredKey = 0;
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
        var held = Held(state);
        if (held != Forbidden && (held & (int)_ownedModifiers) != 0) return 0;

        _down[_ownedKey] = false;
        // No modifiers to go by: a recording's Esc or Delete, which may still be held. An up that
        // never comes costs one later press of that key, and only that key.
        if (_ownedModifiers == Modifiers.None) _recoveredKey = _ownedKey;
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
