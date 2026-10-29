using Orbit.Core.Actions;
using Orbit.Core.Settings;

namespace Orbit.Core.Ring;

/// <summary>A ring layout someone saved under a name.</summary>
public sealed record SavedRing(string Id, string Name, IReadOnlyList<OrbitAction> Slots);

public enum BuiltInRing { Balanced, Screenshots, Everyday }

/// <summary>
/// The ring's layouts (Orbit: RingPage.swift, <c>RingPresets</c>): the ring in use
/// (<c>ring.slots</c>), "My ring" (<c>ring.custom</c>, the user's own layout, kept up to date with
/// every edit so trying a preset never loses it), the built-in presets and the ones saved by name
/// (<c>ring.userPresets</c>). Editing changes the ring and My ring; applying a preset changes only
/// the ring. The same action may sit in more than one slot, as in Orbit.
/// </summary>
public sealed class RingPresets(Preferences preferences)
{
    public const string CustomKey = "ring.custom";
    public const string SavedKey = "ring.userPresets";

    /// <summary>Orbit's presets without its window-snapping one (window snapping is not ported).</summary>
    public static IReadOnlyList<(BuiltInRing Kind, IReadOnlyList<OrbitAction> Slots)> BuiltIn { get; } =
    [
        (BuiltInRing.Balanced, RingSlots.Default),
        (BuiltInRing.Screenshots,
        [
            OrbitAction.CaptureArea, OrbitAction.CaptureText, OrbitAction.CaptureWindow, OrbitAction.CaptureScreen,
            OrbitAction.CaptureRepeat, OrbitAction.PinLastCapture, OrbitAction.Clipboard, OrbitAction.Launcher,
        ]),
        (BuiltInRing.Everyday,
        [
            OrbitAction.Clipboard, OrbitAction.CaptureArea, OrbitAction.Translate, OrbitAction.Launcher,
            OrbitAction.CaptureScreen, OrbitAction.KeepAwake, OrbitAction.RecentCaptures, OrbitAction.CaptureText,
        ]),
    ];

    /// <summary>Stored form of a saved preset.</summary>
    public sealed record Stored(string Id, string Name, string[] Slots);

    /// <summary>The ring in use.</summary>
    public IReadOnlyList<OrbitAction> Current => RingSlots.Parse(preferences.Get<string[]>(Pref.RingSlots));

    /// <summary>My ring; the ring in use until anything was stored, or when what was stored can't be read.</summary>
    public IReadOnlyList<OrbitAction> Custom => RingSlots.TryParse(preferences.Get<string[]>(CustomKey)) ?? Current;

    /// <summary>Saved presets that can still be read (an action that no longer exists makes one unreadable).</summary>
    public IReadOnlyList<SavedRing> Saved =>
        (preferences.Get<Stored[]>(SavedKey) ?? [])
            .Select(s => RingSlots.TryParse(s.Slots) is { } slots ? new SavedRing(s.Id, s.Name, slots) : null)
            .OfType<SavedRing>().ToList();

    /// <summary>An edit on the page: the ring and My ring both.</summary>
    public void Edit(IReadOnlyList<OrbitAction> slots)
    {
        if (!RingSlots.Valid(slots)) return;
        preferences.Set(Pref.RingSlots, RingSlots.Ids(slots));
        preferences.Set(CustomKey, RingSlots.Ids(slots));
    }

    /// <summary>A preset chosen (or an undo): the ring only, My ring stays.</summary>
    public void Apply(IReadOnlyList<OrbitAction> slots)
    {
        if (!RingSlots.Valid(slots)) return;
        // Before the first preset replaces it, the ring in use becomes My ring (Orbit does this on first run).
        if (RingSlots.TryParse(preferences.Get<string[]>(CustomKey)) is null) preferences.Set(CustomKey, RingSlots.Ids(Current));
        preferences.Set(Pref.RingSlots, RingSlots.Ids(slots));
    }

    /// <summary>Saves the ring under a name; an empty name becomes <paramref name="fallbackName"/>.</summary>
    public void Save(string name, IReadOnlyList<OrbitAction> slots, string fallbackName)
    {
        if (!RingSlots.Valid(slots)) return;
        var trimmed = name.Trim();
        Store([.. StoredList(), new Stored(Guid.NewGuid().ToString("N"), trimmed.Length == 0 ? fallbackName : trimmed, RingSlots.Ids(slots))]);
    }

    public void Rename(string id, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return;
        Store(StoredList().Select(s => s.Id == id ? s with { Name = trimmed } : s).ToArray());
    }

    /// <summary>The saved preset takes the ring as it is now, keeping its place and name.</summary>
    public void Update(string id, IReadOnlyList<OrbitAction> slots)
    {
        if (!RingSlots.Valid(slots)) return;
        Store(StoredList().Select(s => s.Id == id ? s with { Slots = RingSlots.Ids(slots) } : s).ToArray());
    }

    public void Delete(string id) => Store(StoredList().Where(s => s.Id != id).ToArray());

    private Stored[] StoredList() => preferences.Get<Stored[]>(SavedKey) ?? [];

    private void Store(Stored[] list) => preferences.Set(SavedKey, list);

    /// <summary>
    /// The ring with <paramref name="count"/> slots: the last ones dropped, or actions not on it
    /// yet added (the defaults first, then the rest, never Settings).
    /// </summary>
    public static IReadOnlyList<OrbitAction> Resize(IReadOnlyList<OrbitAction> slots, int count)
    {
        count = Math.Clamp(count, RingSlots.Min, RingSlots.Max);
        if (count <= slots.Count) return slots.Take(count).ToList();
        var spare = RingSlots.Default.Concat(OrbitActions.All).Where(a => a != OrbitAction.Settings && !slots.Contains(a)).Distinct();
        return [.. slots, .. spare.Take(count - slots.Count)];
    }

    public static IReadOnlyList<OrbitAction> Assign(IReadOnlyList<OrbitAction> slots, int index, OrbitAction action)
    {
        if (index < 0 || index >= slots.Count) return slots;
        var copy = slots.ToList();
        copy[index] = action;
        return copy;
    }

    public static IReadOnlyList<OrbitAction> Swap(IReadOnlyList<OrbitAction> slots, int a, int b)
    {
        if (a < 0 || b < 0 || a >= slots.Count || b >= slots.Count || a == b) return slots;
        var copy = slots.ToList();
        (copy[a], copy[b]) = (copy[b], copy[a]);
        return copy;
    }

    /// <summary>
    /// Where a slot sits, 0 = top, clockwise in eighths (Orbit: <c>slotName</c>), when it sits on
    /// one of the eight compass points; null otherwise (then it is "slot n").
    /// </summary>
    public static int? Compass(int index, int count)
    {
        // Exact in integers: index * 360 / count is a multiple of 45 when index * 8 divides by count.
        return index * 8 % count == 0 ? index * 8 / count % 8 : null;
    }
}
