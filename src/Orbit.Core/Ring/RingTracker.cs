using Orbit.Core.Actions;

namespace Orbit.Core.Ring;

public enum RingEventKind { None, Fire, Close }

public readonly record struct RingEvent(RingEventKind Kind, int Index = -1)
{
    public static readonly RingEvent None = new(RingEventKind.None);
    public static readonly RingEvent Close = new(RingEventKind.Close);
    public static RingEvent Fire(int index) => new(RingEventKind.Fire, index);
}

public enum RingState { Closed, Holding, Tapping }

/// <summary>
/// The ring's choosing, without any drawing (Orbit: Modules/Ring/OrbitRing.swift):
/// <list type="bullet">
/// <item>Hold the trigger, point at an action, let go: it runs.</item>
/// <item>Point past the ring (a flick): it runs at once.</item>
/// <item>A quick tap leaves the ring open; resting on an action then fills it and runs it.</item>
/// </list>
/// Positions are DIPs relative to the ring's center, y down. One ring at a time: after a
/// fire or a close, every call returns <see cref="RingEvent.None"/> until the next <see cref="Open"/>.
/// </summary>
public sealed class RingTracker
{
    public const double Size = 340;
    public const double ItemRadius = 112;
    public const double DeadZone = 38;
    public const double FlickRadius = 175;
    /// <summary>Nothing is chosen until the pointer moves this far: near an edge the ring is offset from it.</summary>
    public const double MoveThreshold = 6;
    /// <summary>Let go sooner than this after opening and the ring stays open (tap mode).</summary>
    public static readonly TimeSpan TapWindow = TimeSpan.FromSeconds(0.35);
    public static readonly TimeSpan DwellGrace = TimeSpan.FromSeconds(0.08);

    private double _openX, _openY;
    private bool _pointerMoved;
    private TimeSpan _openedAt;
    private TimeSpan? _dwellStart;

    public RingTracker(int count, TimeSpan dwell, bool hoverActivates = true)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        Count = count;
        Dwell = dwell < TimeSpan.FromSeconds(0.2) ? TimeSpan.FromSeconds(0.2) : dwell;
        HoverActivates = hoverActivates;
    }

    public int Count { get; }
    public TimeSpan Dwell { get; }
    public bool HoverActivates { get; }

    public RingState State { get; private set; }
    public int? Selection { get; private set; }
    /// <summary>Radians, 0 = up, clockwise; null while the pointer is inside the dead zone or hasn't moved.</summary>
    public double? PointerAngle { get; private set; }
    /// <summary>0 to 1: how far the resting pointer is to running the selection (tap mode only).</summary>
    public double DwellProgress { get; private set; }

    /// <param name="x">Where the pointer is, relative to the center (not 0, 0 when the ring was moved off an edge).</param>
    public void Open(double x, double y, bool hold, TimeSpan now)
    {
        State = hold ? RingState.Holding : RingState.Tapping;
        _openX = x;
        _openY = y;
        _pointerMoved = false;
        _openedAt = now;
        _dwellStart = null;
        Selection = null;
        PointerAngle = null;
        DwellProgress = 0;
    }

    public RingEvent Track(double x, double y, TimeSpan now)
    {
        if (State == RingState.Closed) return RingEvent.None;
        if (!_pointerMoved)
        {
            if (Math.Sqrt((x - _openX) * (x - _openX) + (y - _openY) * (y - _openY)) <= MoveThreshold) return RingEvent.None;
            _pointerMoved = true;
        }
        double distance = Math.Sqrt(x * x + y * y);
        if (distance <= DeadZone)
        {
            PointerAngle = null;
            Select(null, now);
            return RingEvent.None;
        }
        double angle = AngleOf(x, y);
        PointerAngle = angle;
        int index = IndexAt(angle, Count);
        Select(index, now);
        return distance > FlickRadius ? FireAt(index) : RingEvent.None;
    }

    /// <summary>The trigger was let go.</summary>
    public RingEvent Release(TimeSpan now)
    {
        if (State != RingState.Holding) return RingEvent.None;
        State = RingState.Tapping;
        if (Selection is { } index) return FireAt(index);
        if (now - _openedAt > TapWindow) return Finish(RingEvent.Close);
        return RingEvent.None;
    }

    /// <summary>Called every frame: dwell runs the selection in tap mode.</summary>
    public RingEvent Tick(TimeSpan now)
    {
        if (State != RingState.Tapping || !HoverActivates || Selection is not { } index || _dwellStart is not { } start)
        {
            if (State == RingState.Holding) DwellProgress = 0;
            return RingEvent.None;
        }
        DwellProgress = Math.Clamp((now - start - DwellGrace) / Dwell, 0, 1);
        return DwellProgress >= 1 ? FireAt(index) : RingEvent.None;
    }

    /// <summary>A click on an item, or on the hub (null), which closes.</summary>
    public RingEvent Click(int? index) =>
        State == RingState.Closed ? RingEvent.None : index is { } i ? FireAt(i) : Finish(RingEvent.Close);

    /// <summary>Esc, a click outside, the hooks reinstalled.</summary>
    public RingEvent Cancel() => State == RingState.Closed ? RingEvent.None : Finish(RingEvent.Close);

    /// <summary>Radians, 0 = up, clockwise, for y growing downward.</summary>
    public static double AngleOf(double x, double y)
    {
        double angle = Math.Atan2(x, -y);
        return angle < 0 ? angle + 2 * Math.PI : angle;
    }

    /// <summary>The slot an angle points at: slot 0 is centered at the top.</summary>
    public static int IndexAt(double angle, int count)
    {
        double step = 2 * Math.PI / count;
        return (int)((angle + step / 2) / step) % count;
    }

    /// <summary>Items shrink a little past eight so neighbors never touch.</summary>
    public static double ItemScale(int count) => count > 8 ? 8.6 / count : 1;

    private void Select(int? index, TimeSpan now)
    {
        if (index == Selection) return;
        Selection = index;
        DwellProgress = 0;
        _dwellStart = index is null ? null : now;
    }

    private RingEvent FireAt(int index) => Finish(RingEvent.Fire(index));

    private RingEvent Finish(RingEvent e)
    {
        State = RingState.Closed;
        _dwellStart = null;
        DwellProgress = 0;
        return e;
    }
}

/// <summary>The ring's actions, from <c>ring.slots</c> (Orbit's ids).</summary>
public static class RingSlots
{
    public const int Min = 3, Max = 12;

    /// <summary>Orbit's defaults with the window-snapping slots (not ported) given to Windows' nearest neighbors.</summary>
    public static readonly IReadOnlyList<OrbitAction> Default =
    [
        OrbitAction.CaptureArea, OrbitAction.CaptureText, OrbitAction.Translate, OrbitAction.Clipboard,
        OrbitAction.CaptureScreen, OrbitAction.Launcher, OrbitAction.KeepAwake, OrbitAction.CaptureWindow,
    ];

    /// <summary>The stored ids, unknown ones dropped; the defaults unless 3 to 12 remain.</summary>
    public static IReadOnlyList<OrbitAction> Parse(IEnumerable<string>? ids) => TryParse(ids, dropUnknown: true) ?? Default;

    /// <summary>
    /// The stored ids as a ring, or null when it isn't one: fewer than 3 or more than 12, or (unless
    /// <paramref name="dropUnknown"/>) an id no action has.
    /// </summary>
    public static IReadOnlyList<OrbitAction>? TryParse(IEnumerable<string>? ids, bool dropUnknown = false)
    {
        if (ids is null) return null;
        var list = ids.ToList();
        var parsed = list.Select(OrbitActions.FromId).OfType<OrbitAction>().ToList();
        if (!dropUnknown && parsed.Count != list.Count) return null;
        return Valid(parsed) ? parsed : null;
    }

    public static bool Valid(IReadOnlyCollection<OrbitAction> slots) => slots.Count is >= Min and <= Max;

    public static string[] Ids(IEnumerable<OrbitAction> slots) => slots.Select(a => a.Id()).ToArray();
}
