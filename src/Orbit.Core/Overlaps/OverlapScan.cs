using System.Text.Json;
using System.Text.Json.Nodes;
using Orbit.Core.Actions;
using Orbit.Core.Input;

namespace Orbit.Core.Overlaps;

/// <summary>A program sharing a key with Orbit, or doing a job Orbit also does.</summary>
public enum OverlapKind { KeyClash, SameJob }

/// <param name="Program">Who it is, as named by the program.</param>
/// <param name="SlotKey">The Orbit shortcut's place (an action's id, or "ring"), for a key clash.</param>
/// <param name="Keys">The keys both want, for a key clash.</param>
/// <param name="Job">The Orbit action doing the same job, for that kind.</param>
public sealed record Overlap(OverlapKind Kind, string Program, string? SlotKey = null, Hotkey? Keys = null, OrbitAction? Job = null);

/// <summary>What was found on this PC. Anything unreadable is simply absent.</summary>
public sealed record OverlapInput(
    IReadOnlyCollection<string> Running, IReadOnlyCollection<string> Installed, Hotkey? PowerToysRunKey = null);

/// <summary>
/// Which programs overlap with Orbit (Orbit's Overlaps, small): a pure reading of what
/// the app detected. It sees only the programs in <see cref="Programs"/>, and a key they were
/// configured to use only where that setting could be read.
/// </summary>
public static class OverlapScan
{
    private const string PowerToys = "PowerToys", FlowLauncher = "Flow Launcher";
    private const Modifiers CtrlAlt = Modifiers.Ctrl | Modifiers.Alt;
    private static readonly Hotkey AltSpace = new(Modifiers.Alt, HotkeyMatcher.VkSpace);

    /// <summary>The name, its process, a part of its name in the uninstall list, and the job Orbit shares with it.</summary>
    public static readonly IReadOnlyList<(string Name, string Process, string Listed, OrbitAction? Job)> Programs =
    [
        (PowerToys, "PowerToys", "PowerToys", OrbitAction.KeepAwake),
        (FlowLauncher, "Flow.Launcher", "Flow Launcher", OrbitAction.Launcher),
        ("Ditto", "Ditto", "Ditto", OrbitAction.Clipboard),
        ("ShareX", "ShareX", "ShareX", OrbitAction.CaptureArea),
        ("Greenshot", "Greenshot", "Greenshot", OrbitAction.CaptureArea),
        ("EarTrumpet", "EarTrumpet", "EarTrumpet", null),
        ("Twinkle Tray", "Twinkle Tray", "Twinkle Tray", OrbitAction.Displays),
    ];

    private static readonly string[] AlwaysThere = ["Windows", "Xbox Game Bar", "Office", "Browsers, terminals", "Chat apps", "IDEs", "Most programs"];

    /// <param name="input">What was found.</param>
    /// <param name="shortcuts">Orbit's shortcuts now, by place.</param>
    public static List<Overlap> Find(OverlapInput input, IReadOnlyDictionary<string, Hotkey> shortcuts)
    {
        var found = new List<Overlap>();
        var detected = Programs.Where(p => IsPresent(p, input)).ToList();
        bool powerToys = detected.Any(p => p.Name == PowerToys), flow = detected.Any(p => p.Name == FlowLauncher);

        foreach (var (slot, keys) in shortcuts)
        {
            foreach (var owner in ShortcutRules.KnownOwners(keys))
            {
                if (owner is "Word" or "OneNote" && PassThroughRule.Passes(PassThroughRule.Defaults, owner == "Word" ? "winword.exe" : "onenote.exe", keys)) continue;
                // PowerToys Run moved to other keys in its settings: its default is no longer a clash.
                if (owner == "PowerToys Run" && input.PowerToysRunKey is not null) continue;
                if (owner.StartsWith(PowerToys, StringComparison.Ordinal) ? powerToys : AlwaysThere.Contains(owner) || owner is "Word" or "OneNote")
                    found.Add(new Overlap(OverlapKind.KeyClash, owner, slot, keys));
            }
            if (powerToys && input.PowerToysRunKey == keys) found.Add(new Overlap(OverlapKind.KeyClash, "PowerToys Run", slot, keys));
            if (flow && keys == AltSpace) found.Add(new Overlap(OverlapKind.KeyClash, FlowLauncher, slot, keys));
        }
        foreach (var program in detected)
            if (program.Job is { } job) found.Add(new Overlap(OverlapKind.SameJob, program.Name, Job: job));
        return found;
    }

    private static bool IsPresent((string Name, string Process, string Listed, OrbitAction? Job) program, OverlapInput input) =>
        input.Running.Contains(program.Process, StringComparer.OrdinalIgnoreCase)
        || input.Installed.Any(name => name.Contains(program.Listed, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Other keys to offer: Ctrl+Alt+L for the launcher first (docs/plan/04-shortcuts.md), else the
    /// first Ctrl+Alt+letter nobody on the known list uses. Free only as far as Orbit can tell.
    /// </summary>
    public static Hotkey? Suggest(string slot, IEnumerable<Hotkey> taken)
    {
        var used = taken.ToHashSet();
        foreach (char letter in (slot == OrbitAction.Launcher.Id() ? "L" : "") + "LJNOBGHIMQUWYZ")
        {
            var keys = new Hotkey(CtrlAlt, letter);
            if (!used.Contains(keys) && ShortcutRules.Check(keys) == ShortcutProblem.None && ShortcutRules.KnownOwners(keys).Count == 0) return keys;
        }
        return null;
    }

    /// <summary>PowerToys Run's activation keys from its settings file; null when they can't be read.</summary>
    public static Hotkey? ParsePowerToysRun(string json)
    {
        try
        {
            if (JsonNode.Parse(json)?["properties"]?["open_powerlauncher"] is not JsonObject keys) return null;
            var modifiers = Modifiers.None;
            if (keys["win"]?.GetValue<bool>() == true) modifiers |= Modifiers.Win;
            if (keys["ctrl"]?.GetValue<bool>() == true) modifiers |= Modifiers.Ctrl;
            if (keys["alt"]?.GetValue<bool>() == true) modifiers |= Modifiers.Alt;
            if (keys["shift"]?.GetValue<bool>() == true) modifiers |= Modifiers.Shift;
            int code = keys["code"]?.GetValue<int>() ?? 0;
            return code is > 0 and < 256 ? new Hotkey(modifiers, (uint)code) : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

}
