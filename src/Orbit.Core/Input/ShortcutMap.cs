using System.Text.Json;

namespace Orbit.Core.Input;

/// <summary>A shortcut's place: its key in settings (an action's id, or "ring") and its default, if it has one.</summary>
public sealed record ShortcutSlot(string Key, Hotkey? Default);

/// <summary>Why a key can't be a shortcut.</summary>
public enum ShortcutProblem { None, NeedsModifier, InputKey, Reserved }

/// <summary>What may be a shortcut, and who else is known to use a key (docs/plan/04-shortcuts.md).</summary>
public static class ShortcutRules
{
    private const uint VkHangul = 0x15, VkHanja = 0x19, VkEscape = 0x1B, VkTab = 0x09, VkDelete = 0x2E, VkF4 = 0x73, VkL = 0x4C, VkPrintScreen = 0x2C;
    private const uint VkLeft = 0x25, VkUp = 0x26, VkDown = 0x28, VkBacktick = 0xC0;

    private const Modifiers Ctrl = Modifiers.Ctrl, Alt = Modifiers.Alt, Shift = Modifiers.Shift, Win = Modifiers.Win;

    /// <summary>Ctrl, Alt or Win is required: Shift alone or a bare key would take typing away.</summary>
    public static ShortcutProblem Check(Hotkey hotkey)
    {
        if (hotkey.Key is VkHangul or VkHanja) return ShortcutProblem.InputKey;
        if ((hotkey.Modifiers & (Ctrl | Alt | Win)) == 0) return ShortcutProblem.NeedsModifier;
        if (hotkey.Key == VkEscape) return ShortcutProblem.Reserved;
        if (hotkey.Key == VkTab && hotkey.Modifiers.HasFlag(Alt)) return ShortcutProblem.Reserved;
        if (hotkey.Key == VkF4 && hotkey.Modifiers == Alt) return ShortcutProblem.Reserved;
        if (hotkey.Key == VkDelete && hotkey.Modifiers == (Ctrl | Alt)) return ShortcutProblem.Reserved;
        if (hotkey.Key == VkL && hotkey.Modifiers == Win) return ShortcutProblem.Reserved;
        return ShortcutProblem.None;
    }

    private static readonly (Modifiers Modifiers, uint First, uint Last, string Owner)[] Known =
    [
        (Win, 'V', 'V', "Windows"), (Win, 0x20, 0x20, "Windows"), (Win | Shift, 'S', 'S', "Windows"), (Win | Shift, 'R', 'R', "Windows"),
        (Win | Shift, 'V', 'V', "Windows"), (Win | Shift, 'T', 'T', "Windows"), (Win | Shift, '0', '9', "Windows"),
        (Win | Alt, 'B', 'B', "Windows"), (Win | Alt, 'D', 'D', "Windows"), (Win | Alt, 'H', 'H', "Windows"), (Win | Alt, 'K', 'K', "Windows"),
        (Win | Alt, VkUp, VkUp, "Windows"), (Win | Alt, VkDown, VkDown, "Windows"), (Win | Alt, '0', '9', "Windows"),
        (Win | Alt, 'R', 'R', "Xbox Game Bar"), (Win | Alt, 'G', 'G', "Xbox Game Bar"), (Win | Alt, VkPrintScreen, VkPrintScreen, "Xbox Game Bar"),
        (Win | Ctrl, 'C', 'C', "Windows"), (Win | Ctrl, 'D', 'D', "Windows"), (Win | Ctrl, 'F', 'F', "Windows"), (Win | Ctrl, 'Q', 'Q', "Windows"), (Win | Ctrl, 'V', 'V', "Windows"),
        (Ctrl | Alt, VkTab, VkTab, "Windows"), (Ctrl, 0x20, 0x20, "Windows"), (Alt, VkPrintScreen, VkPrintScreen, "Windows"), (Win, VkPrintScreen, VkPrintScreen, "Windows"),
        (Alt, 0x20, 0x20, "PowerToys Run"), (Win | Alt, 0x20, 0x20, "PowerToys Command Palette"), (Win | Shift, 'V', 'V', "PowerToys Advanced Paste"),
        (Win | Ctrl | Alt, 'V', 'V', "PowerToys"), (Win | Shift, 'C', 'C', "PowerToys Color Picker"), (Win | Shift, 'T', 'T', "PowerToys Text Extractor"),
        (Win | Ctrl, 'T', 'T', "PowerToys Always On Top"), (Win | Ctrl | Shift, 'M', 'M', "PowerToys Screen Ruler"), (Win | Ctrl | Shift, 'T', 'T', "PowerToys Crop And Lock"),
        (Win | Ctrl | Shift, 'R', 'R', "PowerToys Crop And Lock"), (Win | Ctrl, VkBacktick, VkBacktick, "PowerToys Workspaces"), (Win | Shift, VkBacktick, VkBacktick, "PowerToys FancyZones"),
        (Win | Shift, 'H', 'H', "PowerToys Mouse Highlighter"), (Ctrl, '1', '7', "PowerToys ZoomIt"),
        (Ctrl | Shift, 'V', 'V', "Browsers, terminals"), (Ctrl | Alt, 'V', 'V', "Office"), (Ctrl | Alt, '1', '3', "Word"), (Ctrl | Alt, '1', '6', "OneNote"),
        (Ctrl | Alt, 'T', 'T', "Word"), (Ctrl | Alt, 'C', 'C', "Word"), (Ctrl | Alt, 'K', 'K', "Word"), (Ctrl | Alt, 'S', 'S', "Word"), (Ctrl | Alt, 'A', 'A', "Chat apps"),
        (Shift | Alt, 'A', 'Z', "IDEs"), (Ctrl, 'A', 'Z', "Most programs"),
    ];

    /// <summary>Who else is known to use this key, in table order; empty when nobody is.</summary>
    public static IReadOnlyList<string> KnownOwners(Hotkey hotkey) =>
        Known.Where(k => k.Modifiers == hotkey.Modifiers && hotkey.Key >= k.First && hotkey.Key <= k.Last).Select(k => k.Owner).Distinct().ToList();
}

/// <summary>
/// The user's shortcuts: stored as a difference from the defaults (a key to a shortcut, or to null
/// for none), so a default added later still reaches them. A stored entry that makes no sense is
/// dropped and the default stays.
/// </summary>
public static class ShortcutMap
{
    public static Dictionary<string, Hotkey?> Parse(string? json)
    {
        var overrides = new Dictionary<string, Hotkey?>();
        if (string.IsNullOrWhiteSpace(json)) return overrides;
        Dictionary<string, string?>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
        }
        catch (JsonException)
        {
            Log.Warn("Shortcut settings were unreadable; using the defaults");
            return overrides;
        }
        foreach (var (key, value) in stored ?? [])
        {
            if (value is null) overrides[key] = null;
            else if (Decode(value) is { } hotkey && ShortcutRules.Check(hotkey) == ShortcutProblem.None) overrides[key] = hotkey;
            else Log.Warn($"A stored shortcut for {key} was dropped");
        }
        return overrides;
    }

    public static string Serialize(IReadOnlyDictionary<string, Hotkey?> overrides) =>
        JsonSerializer.Serialize(overrides.ToDictionary(o => o.Key, o => o.Value is { } hotkey ? Encode(hotkey) : null));

    private static string Encode(Hotkey hotkey) => $"{(int)hotkey.Modifiers}:{hotkey.Key}";

    private static Hotkey? Decode(string value)
    {
        var parts = value.Split(':');
        return parts.Length == 2 && int.TryParse(parts[0], out int modifiers) && modifiers is >= 0 and <= 15
            && uint.TryParse(parts[1], out uint key) && key is > 0 and < 256 && !HotkeyMatcher.IsModifier(key)
            ? new Hotkey((Modifiers)modifiers, key)
            : null;
    }

    /// <summary>
    /// The shortcut each key has now. The user's choices come first: a default that collides with
    /// one gives way, and of two equal choices (a hand-edited file) the earlier slot keeps it.
    /// </summary>
    public static Dictionary<string, Hotkey> Resolve(IEnumerable<ShortcutSlot> slots, IReadOnlyDictionary<string, Hotkey?> overrides)
    {
        var list = slots.ToList();
        var result = new Dictionary<string, Hotkey>();
        var taken = new HashSet<Hotkey>();
        foreach (var slot in list)
            if (overrides.TryGetValue(slot.Key, out var chosen) && chosen is { } hotkey && taken.Add(hotkey))
                result[slot.Key] = hotkey;
        foreach (var slot in list)
            if (!overrides.ContainsKey(slot.Key) && slot.Default is { } hotkey && taken.Add(hotkey))
                result[slot.Key] = hotkey;
        return result;
    }
}
