using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orbit.Core.Scratchpad;

/// <summary>One tab of the pad.</summary>
public sealed record Pad(Guid Id, string Name, string Text, DateTimeOffset Modified);

/// <summary>
/// The pad's tabs, the selected one and the recently closed ones (behavior after Vorssaint's
/// Scratchpad; Orbit has none). Closing a tab never loses it at once: it waits in
/// <see cref="Closed"/>, newest first, until <see cref="ClosedLimit"/> newer ones push it out.
/// </summary>
public sealed class ScratchpadDocument
{
    public const int ClosedLimit = 20;

    public List<Pad> Pads { get; set; } = [];
    public Guid Selected { get; set; }
    public List<Pad> Closed { get; set; } = [];

    [JsonIgnore]
    public Pad Current => Pads.First(p => p.Id == Selected);

    public static ScratchpadDocument New(string name, DateTimeOffset now)
    {
        var pad = new Pad(Guid.NewGuid(), name, "", now);
        return new ScratchpadDocument { Pads = [pad], Selected = pad.Id };
    }

    /// <summary>Repairs what a hand-edited or older file may hold: no tabs, repeated ids, a missing selection, null text.</summary>
    public ScratchpadDocument Sanitized(Func<int, string> defaultName, DateTimeOffset now)
    {
        var seen = new HashSet<Guid>();
        Pad Clean(Pad p, int index) => p with
        {
            Id = p.Id == Guid.Empty || !seen.Add(p.Id) ? Guid.NewGuid() : p.Id,
            Name = string.IsNullOrWhiteSpace(p.Name) ? defaultName(index + 1) : p.Name.Trim(),
            Text = p.Text ?? "",
        };
        var pads = (Pads ?? []).Where(p => p is not null).Select(Clean).ToList();
        var closed = (Closed ?? []).Where(p => p is not null).Select(Clean).Take(ClosedLimit).ToList();
        if (pads.Count == 0) pads.Add(new Pad(Guid.NewGuid(), defaultName(1), "", now));
        var selected = pads.Any(p => p.Id == Selected) ? Selected : pads[0].Id;
        return new ScratchpadDocument { Pads = pads, Selected = selected, Closed = closed };
    }

    /// <summary>A name not taken yet: "메모 2", "메모 3", …</summary>
    public string NextName(Func<int, string> name)
    {
        for (int n = Pads.Count + 1; ; n++)
            if (Pads.All(p => p.Name != name(n))) return name(n);
    }

    public Pad Add(string name, DateTimeOffset now)
    {
        var pad = new Pad(Guid.NewGuid(), name, "", now);
        int at = Pads.FindIndex(p => p.Id == Selected);
        Pads.Insert(at < 0 ? Pads.Count : at + 1, pad);
        Selected = pad.Id;
        return pad;
    }

    /// <summary>Closes a tab; one with text is kept in <see cref="Closed"/>. The last tab can't be closed.</summary>
    public bool Close(Guid id)
    {
        int at = Pads.FindIndex(p => p.Id == id);
        if (at < 0 || Pads.Count == 1) return false;
        var pad = Pads[at];
        Pads.RemoveAt(at);
        if (pad.Text.Length > 0) Remember(pad);
        if (Selected == id) Selected = Pads[Math.Min(at, Pads.Count - 1)].Id;
        return true;
    }

    /// <summary>Brings the most recently closed tab back, beside the selected one.</summary>
    public Pad? Reopen()
    {
        if (Closed.Count == 0) return null;
        var pad = Closed[0];
        Closed.RemoveAt(0);
        int at = Pads.FindIndex(p => p.Id == Selected);
        Pads.Insert(at < 0 ? Pads.Count : at + 1, pad);
        Selected = pad.Id;
        return pad;
    }

    public void Update(Guid id, Func<Pad, Pad> change)
    {
        int at = Pads.FindIndex(p => p.Id == id);
        if (at >= 0) Pads[at] = change(Pads[at]);
    }

    /// <summary>
    /// Closes tabs unedited for longer than <paramref name="days"/> (0 keeps everything); they go
    /// to <see cref="Closed"/> like any closed tab. The selected tab and the last one stay, and so
    /// does a tab with text when the closed list is full: tidying up never pushes a note out for
    /// good. A modified time in the future (a clock set back) counts as now.
    /// </summary>
    public int ApplyRetention(int days, DateTimeOffset now)
    {
        if (days <= 0) return 0;
        int closed = 0;
        foreach (var pad in Pads.Where(p => p.Id != Selected && now - Min(p.Modified, now) > TimeSpan.FromDays(days)).ToList())
        {
            if (pad.Text.Length > 0 && Closed.Count >= ClosedLimit) continue;
            if (Close(pad.Id)) closed++;
        }
        return closed;
    }

    /// <summary>Every tab's text, open ones first: the last resort when nothing can be written.</summary>
    public string AllText() => string.Join("\n\n", Pads.Concat(Closed).Where(p => p.Text.Length > 0).Select(p => $"# {p.Name}\n{p.Text}"));

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private void Remember(Pad pad)
    {
        Closed.Insert(0, pad);
        if (Closed.Count > ClosedLimit) Closed.RemoveRange(ClosedLimit, Closed.Count - ClosedLimit);
    }

    public ScratchpadDocument Copy() => new() { Pads = [.. Pads], Selected = Selected, Closed = [.. Closed] };

    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public string Serialize() => JsonSerializer.Serialize(this, Json);
}
