using System.Text.Json;

namespace Orbit.Core.Displays;

/// <summary>Brightness levels on every screen at once, by display key (Orbit: DisplayManager.Preset).</summary>
public sealed record BrightnessPreset(string Name, Dictionary<string, double> Levels);

/// <summary>The arithmetic behind the Displays feature: levels, steps, and matching Windows' names for a screen.</summary>
public static class DisplayLevels
{
    /// <summary>Orbit's step for the brightness actions: 1/16 of the range.</summary>
    public const double Step = 1.0 / 16;

    /// <summary>A 0–1 level as the monitor's own value, out of its maximum.</summary>
    public static uint ToRaw(double level, uint max) => (uint)Math.Round(Math.Clamp(level, 0, 1) * max);

    /// <summary>The monitor's value as 0–1; null when the maximum is no maximum.</summary>
    public static double? ToLevel(uint current, uint max) => max == 0 ? null : Math.Clamp((double)current / max, 0, 1);

    /// <summary>One step up or down from the nearest step, as Orbit's keys move (0.30 up is 0.375, not 0.3625).</summary>
    public static double Stepped(double level, bool up) =>
        Math.Clamp(Math.Round(level / Step) * Step + (up ? Step : -Step), 0, 1);

    /// <summary>The supported percentage nearest a level (the built-in panel may only take some).</summary>
    public static byte Nearest(double level, IReadOnlyList<byte> supported)
    {
        byte wanted = (byte)Math.Round(Math.Clamp(level, 0, 1) * 100);
        if (supported.Count == 0) return wanted;
        return supported.MinBy(s => Math.Abs(s - wanted));
    }

    /// <summary>
    /// A monitor's device path (<c>\\?\DISPLAY#BOE0867#4&amp;1a2b&amp;0&amp;UID8388688#{e6f07b5f-…}</c>)
    /// and a WMI instance name (<c>DISPLAY\BOE0867\4&amp;1a2b&amp;0&amp;UID8388688_0</c>) as the same
    /// comparable form: <c>DISPLAY\BOE0867\4&amp;1A2B&amp;0&amp;UID8388688</c>.
    /// </summary>
    public static string HardwareKey(string name)
    {
        var key = name.StartsWith(@"\\?\", StringComparison.Ordinal) ? name[4..] : name;
        int guid = key.IndexOf("#{", StringComparison.Ordinal);
        if (guid >= 0) key = key[..guid];
        key = key.Replace('#', '\\');
        // WMI adds the instance's index to the device's.
        int underscore = key.LastIndexOf('_');
        if (underscore > key.LastIndexOf('\\') && key[(underscore + 1)..].All(char.IsAsciiDigit)) key = key[..underscore];
        return key.ToUpperInvariant();
    }

    public static List<BrightnessPreset> ReadPresets(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<List<BrightnessPreset>>(json) ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p.Name) && p.Levels is not null).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string WritePresets(IEnumerable<BrightnessPreset> presets) => JsonSerializer.Serialize(presets);
}
