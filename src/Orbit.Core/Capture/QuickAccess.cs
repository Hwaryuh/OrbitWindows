using Orbit.Core.Display;

namespace Orbit.Core.Capture;

/// <summary>The corner of the work area the preview cards sit in (the <c>capture.thumbnailCorner</c> pref).</summary>
public readonly record struct CardCorner(bool OnLeft, bool OnTop)
{
    /// <summary>The four values the pref takes, as Orbit's <c>QuickAccess.corners</c>.</summary>
    public static readonly string[] Names = ["bottomLeft", "bottomRight", "topLeft", "topRight"];

    /// <summary>Orbit reads "…Right" as right and "top…" as top; anything else is the bottom left there.
    /// This port's default is the bottom right, so only an exact name moves the cards.</summary>
    public static CardCorner Parse(string? name) => name switch
    {
        "bottomLeft" => new(true, false),
        "topLeft" => new(true, true),
        "topRight" => new(false, true),
        _ => new(false, false),
    };
}

/// <summary>
/// Sizes of the preview cards (Orbit: Design/Theme.swift <c>Size.thumbnail*</c>, QuickAccess.swift
/// <c>height(for:)</c>, <c>layout()</c>). Everything here is in DIPs except where a method says pixels.
/// </summary>
public static class CardLayout
{
    public const double Width = 240;
    public const double MaxHeight = 180;
    public const double MinHeight = 96;
    /// <summary>From the work area's edge to the cards.</summary>
    public const double EdgeOffset = 20;
    /// <summary>Room around the cards inside the panel, for the shadow.</summary>
    public const double Padding = 16;
    public const double Gap = 12;
    /// <summary>The picture's margin inside its card.</summary>
    public const double Inset = 8;
    public const double TabWidth = 30, TabHeight = 64;

    /// <summary>A card's height for a picture of <paramref name="width"/> × <paramref name="height"/>.</summary>
    public static double Height(double width, double height) =>
        width <= 0 ? MaxHeight : Math.Min(Math.Max(Width * height / width, MinHeight), MaxHeight);

    /// <summary>The picture's size in its card: whole, never cropped or blown up past its own size.</summary>
    public static (double Width, double Height) Fit(double width, double height)
    {
        if (width <= 0 || height <= 0) return (0, 0);
        double maxW = Math.Min(Width - 2 * Inset, width), maxH = Math.Min(Height(width, height) - 2 * Inset, height);
        double k = Math.Min(maxW / width, maxH / height);
        return (width * k, height * k);
    }

    /// <summary>A small picture shows at its real size with its pixel size written on it.</summary>
    public static bool IsSmall(double width, double height) =>
        width < Width * 0.8 && height < Height(width, height) * 0.8;

    /// <summary>
    /// How many of the cards (newest first, by height) fit in a work area <paramref name="workHeight"/>
    /// DIPs tall; at least one.
    /// </summary>
    public static int Fitting(IReadOnlyList<double> heights, double workHeight)
    {
        double room = workHeight - 2 * EdgeOffset;
        double used = 0;
        for (int i = 0; i < heights.Count; i++)
        {
            used += heights[i] + (i > 0 ? Gap : 0);
            if (used > room) return Math.Max(1, i);
        }
        return heights.Count;
    }

    /// <summary>The panel's size in DIPs for cards of these heights.</summary>
    public static (double Width, double Height) PanelSize(IReadOnlyList<double> heights) =>
        (Width + 2 * Padding, heights.Sum() + Gap * Math.Max(0, heights.Count - 1) + 2 * Padding);

    /// <summary>
    /// Where the panel goes, in physical pixels: <paramref name="dipWidth"/> × <paramref name="dipHeight"/>
    /// at the monitor's <paramref name="scale"/>, its cards <see cref="EdgeOffset"/> in from the corner
    /// of <paramref name="work"/>. Tucked, it is the tab flush with the side edge.
    /// </summary>
    public static PixelRect Place(PixelRect work, double scale, CardCorner corner, double dipWidth, double dipHeight, bool tucked)
    {
        int w = (int)Math.Ceiling(dipWidth * scale), h = (int)Math.Ceiling(dipHeight * scale);
        int edge = (int)Math.Round(EdgeOffset * scale);
        int pad = tucked ? 0 : (int)Math.Round(Padding * scale);
        int side = tucked ? 0 : edge - pad;
        int x = corner.OnLeft ? work.X + side : work.Right - side - w;
        int y = corner.OnTop ? work.Y + edge - pad : work.Bottom - edge + pad - h;
        return new PixelRect(x, y, w, h);
    }
}

/// <summary>
/// The preview cards' state (Orbit: QuickAccess.swift): newest first, at most <see cref="MaxVisible"/>.
/// Kept cards (back from the editor or a pin) stay until closed; nothing leaves by itself while the
/// stack is tucked, a card is hovered, or a card is held (dragged out, its menu open, work running).
/// Timers are the caller's: it asks <see cref="AutoDismissDue"/> when one fires.
/// </summary>
public sealed class CardStack<T> where T : class
{
    public const int MaxVisible = 3;

    private readonly List<T> _items = [];
    private readonly HashSet<T> _kept = [];
    private readonly HashSet<T> _hovered = [];
    private readonly Dictionary<T, int> _held = [];
    private readonly HashSet<T> _closeWhenReleased = [];

    public IReadOnlyList<T> Items => _items;
    public bool Tucked { get; private set; }

    /// <summary>Adds a card on top; returns the cards pushed out (the oldest ones that are not held).</summary>
    public IReadOnlyList<T> Add(T item, bool keep = false)
    {
        _items.Remove(item);
        _items.Insert(0, item);
        if (keep) _kept.Add(item);
        var removed = new List<T>();
        for (int i = _items.Count - 1; i >= 0 && _items.Count > MaxVisible; i--)
        {
            if (_held.ContainsKey(_items[i])) continue;
            removed.Add(_items[i]);
            Forget(_items[i]);
            _items.RemoveAt(i);
        }
        return removed;
    }

    /// <summary>Takes a card off. A held card goes when it is released; false until then.</summary>
    public bool Dismiss(T item)
    {
        if (!_items.Contains(item)) return false;
        if (_held.ContainsKey(item))
        {
            _closeWhenReleased.Add(item);
            return false;
        }
        _items.Remove(item);
        Forget(item);
        if (_items.Count == 0) Tucked = false;
        return true;
    }

    public void SetTucked(bool tucked) => Tucked = tucked && _items.Count > 0;

    public void SetHover(T item, bool on)
    {
        if (on) _hovered.Add(item);
        else _hovered.Remove(item);
    }

    public bool IsKept(T item) => _kept.Contains(item);

    /// <summary>The card is in use (a drag, its menu, copying its text); holds nest.</summary>
    public void Hold(T item) => _held[item] = _held.GetValueOrDefault(item) + 1;

    /// <summary>Ends one hold. True when the card was dismissed while held and is gone now.</summary>
    public bool Release(T item)
    {
        if (!_held.TryGetValue(item, out int count)) return false;
        if (count > 1)
        {
            _held[item] = count - 1;
            return false;
        }
        _held.Remove(item);
        return _closeWhenReleased.Remove(item) && Dismiss(item);
    }

    /// <summary>Whether a card's timer, when it fires, takes the card away.</summary>
    public bool AutoDismissDue(T item, double seconds) =>
        seconds > 0 && !Tucked && _items.Contains(item) && !_kept.Contains(item) && !_hovered.Contains(item) && !_held.ContainsKey(item);

    /// <summary>Whether a timer should run for the card at all (Orbit: <c>scheduleDismiss</c>).</summary>
    public bool ShouldSchedule(T item, double seconds) => seconds > 0 && !Tucked && !_kept.Contains(item);

    private void Forget(T item)
    {
        _kept.Remove(item);
        _hovered.Remove(item);
        _closeWhenReleased.Remove(item);
    }
}

/// <summary>What a press on a card turns into (Orbit: <c>SwipeOrDrag</c>).</summary>
public enum CardGestureKind { Pending, Swipe, Drag }

/// <summary>
/// One mouse gesture, three meanings, as macOS's own screenshot preview: a click, a swipe toward
/// the screen edge (dismiss), or a drag any other way (the file goes out with the pointer). DIPs.
/// </summary>
public sealed class CardGesture(double edge)
{
    /// <summary>Movement before a press stops being a click.</summary>
    public const double Slop = 5;
    /// <summary>A swipe this far toward the edge dismisses the card.</summary>
    public const double DismissDistance = 70;

    /// <summary>-1 when the nearest screen edge is on the left, 1 on the right.</summary>
    public double Edge { get; } = edge;
    public CardGestureKind Kind { get; private set; }

    /// <summary>Called with the movement since the press; returns the kind, decided once past <see cref="Slop"/>.</summary>
    public CardGestureKind Move(double dx, double dy)
    {
        if (Kind == CardGestureKind.Pending && Math.Sqrt(dx * dx + dy * dy) > Slop)
            Kind = dx * Edge > 0 && Math.Abs(dx) > Math.Abs(dy) ? CardGestureKind.Swipe : CardGestureKind.Drag;
        return Kind;
    }

    /// <summary>How far the card follows a swipe: fully toward the edge, a fifth the other way.</summary>
    public double Offset(double dx) => dx * Edge > 0 ? dx : dx * 0.2;

    /// <summary>At release: whether the swipe went far enough to dismiss.</summary>
    public bool Dismisses(double dx) => Kind == CardGestureKind.Swipe && dx * Edge > DismissDistance;
}
