using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Orbit.App.Design;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Actions;
using Orbit.Core.Display;
using Orbit.Core.Ring;

using static Orbit.App.Features.Ring.RingDrawing;

namespace Orbit.App.Features.Ring;

/// <summary>
/// The ring around the pointer (Orbit: OrbitRing.swift, RingView): a disc with the actions on a
/// dotted orbit, a satellite riding toward the pointer, and the hub naming the choice. Choosing is
/// <see cref="RingTracker"/>'s; this window draws it and feeds it the pointer every frame. Never
/// takes focus; clicks on items and the hub land, clicks elsewhere pass through.
/// </summary>
internal sealed class RingWindow : OverlayWindow
{
    private const double Size = RingTracker.Size;
    private const double Center = Size / 2;
    private const double Disc = Size - 40;
    private const double Hub = 92;

    private static readonly DependencyProperty AppearProperty = DependencyProperty.Register(
        nameof(Appear), typeof(double), typeof(RingWindow), new PropertyMetadata(0.0, (d, _) => ((RingWindow)d).Layout()));

    private readonly RingTracker _tracker;
    private readonly IReadOnlyList<OrbitAction> _slots;
    private readonly Palette _palette;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ScaleTransform _rootScale = new(1, 1, Center, Center);
    private readonly Item[] _items;
    private readonly Path _sector;
    private readonly Ellipse _satellite;
    private readonly ContentControl _hub;
    private readonly OrbitMark _mark;
    private MonitorInfo _monitor;
    private int _centerX, _centerY;
    private int? _shownSelection = -1;
    private bool _finished;

    private RingWindow(IReadOnlyList<OrbitAction> slots, bool hold, TimeSpan dwell, bool hoverActivates, Palette palette)
        : base(activating: false, transparent: true, clickThrough: false)
    {
        _slots = slots;
        _palette = palette;
        _tracker = new RingTracker(slots.Count, dwell, hoverActivates);
        _items = new Item[slots.Count];

        var root = new Canvas { Width = Size, Height = Size, RenderTransform = _rootScale };
        var disc = new Ellipse
        {
            Width = Disc, Height = Disc,
            Fill = Palette.Fade(palette.Canvas, 0.96), Stroke = palette.Hairline, StrokeThickness = 1,
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 12, Direction = 270, Opacity = 0.28, Color = Colors.Black },
        };
        RingDrawing.Place(root, disc, Center, Center);
        root.Children.Add(Spokes(slots.Count, Center, 44, Size / 2 - 24, SpokeStroke(palette)));
        var track = new Ellipse
        {
            Width = RingTracker.ItemRadius * 2, Height = RingTracker.ItemRadius * 2,
            Stroke = Palette.Fade(palette.Ink, 0.14), StrokeThickness = 1, StrokeDashArray = [2, 4],
        };
        RingDrawing.Place(root, track, Center, Center);
        _sector = new Path { Fill = Palette.Fade(palette.Ink, 0.06), Visibility = Visibility.Collapsed };
        root.Children.Add(_sector);
        _satellite = new Ellipse
        {
            Width = 13, Height = 13, Fill = palette.Accent, Stroke = palette.Canvas, StrokeThickness = 2, Visibility = Visibility.Collapsed,
        };
        root.Children.Add(_satellite);

        double itemScale = RingTracker.ItemScale(slots.Count);
        for (int i = 0; i < slots.Count; i++)
        {
            var item = new Item(slots[i], itemScale, palette);
            int index = i;
            item.Root.MouseLeftButtonUp += (_, e) => { e.Handled = true; Finish(_tracker.Click(index)); };
            root.Children.Add(item.Root);
            _items[i] = item;
        }

        var hubDisc = new Ellipse { Width = Hub, Height = Hub, Fill = palette.Surface, Stroke = palette.Hairline, StrokeThickness = 1 };
        RingDrawing.Place(root, hubDisc, Center, Center);
        // Centered by itself, as the hub's text is: the hub's content alignment left it in the top left corner.
        _mark = new OrbitMark { Size = 46, Ink = palette.Ink, Accent = palette.Accent, Dot = true, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _hub = new ContentControl { Width = 80, Content = _mark, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Height = Hub };
        RingDrawing.Place(root, _hub, Center, Center);
        hubDisc.MouseLeftButtonUp += (_, e) => { e.Handled = true; Finish(_tracker.Click(null)); };
        _hub.MouseLeftButtonUp += (_, e) => { e.Handled = true; Finish(_tracker.Click(null)); };

        Content = root;
        _monitor = Monitors.FromPoint(0, 0)!;
        var (x, y) = Monitors.Cursor();
        PlaceAround(x, y);
        _tracker.Open((x - _centerX) / _monitor.Scale, (y - _centerY) / _monitor.Scale, hold, _clock.Elapsed);
        Log.Info($"Ring opened at ({_centerX},{_centerY}) on {_monitor.DeviceName}, pointer ({x},{y}), {(hold ? "held" : "tapped")}, {slots.Count} slots");
        Layout();
    }

    /// <summary>The ring's choice or its closing; raised once.</summary>
    public event Action<RingEvent>? Finished;

    private double Appear
    {
        get => (double)GetValue(AppearProperty);
        set => SetValue(AppearProperty, value);
    }

    public static RingWindow Open(IReadOnlyList<OrbitAction> slots, bool hold, TimeSpan dwell, bool hoverActivates)
    {
        var ring = new RingWindow(slots, hold, dwell, hoverActivates, Palette.Current());
        ring.ShowAt(new PixelRect(ring._centerX - ring.SizePixels / 2, ring._centerY - ring.SizePixels / 2, ring.SizePixels, ring.SizePixels));
        ring.Animate(1, TimeSpan.FromMilliseconds(320), new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 });
        CompositionTarget.Rendering += ring.OnFrame;
        return ring;
    }

    /// <summary>The trigger was let go.</summary>
    public void Release() => Finish(_tracker.Release(_clock.Elapsed));

    public void Cancel() => Finish(_tracker.Cancel());

    /// <summary>A press anywhere (physical pixels): outside the disc it closes the ring.</summary>
    public void PressedAt(int x, int y)
    {
        double dx = (x - _centerX) / _monitor.Scale, dy = (y - _centerY) / _monitor.Scale;
        if (Math.Sqrt(dx * dx + dy * dy) > Disc / 2) Cancel();
    }

    private int SizePixels => (int)Math.Round(Size * _monitor.Scale);

    /// <summary>Centered on the pointer, moved in from the monitor's edges so the whole ring shows.</summary>
    private void PlaceAround(int x, int y)
    {
        _monitor = Monitors.FromPoint(x, y) ?? _monitor;
        int half = SizePixels / 2;
        var bounds = _monitor.Bounds;
        _centerX = Math.Clamp(x, bounds.X + half, Math.Max(bounds.X + half, bounds.Right - half));
        _centerY = Math.Clamp(y, bounds.Y + half, Math.Max(bounds.Y + half, bounds.Bottom - half));
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var (x, y) = Monitors.Cursor();
        var now = _clock.Elapsed;
        var result = _tracker.Track((x - _centerX) / _monitor.Scale, (y - _centerY) / _monitor.Scale, now);
        if (result.Kind == RingEventKind.None) result = _tracker.Tick(now);
        Refresh();
        if (result.Kind != RingEventKind.None) Finish(result);
    }

    private void Finish(RingEvent result)
    {
        if (result.Kind == RingEventKind.None || _finished) return;
        _finished = true;
        CompositionTarget.Rendering -= OnFrame;
        Log.Info($"Ring finished: {result.Kind} {result.Index} after {_clock.ElapsedMilliseconds} ms");
        Finished?.Invoke(result);
        Animate(0, TimeSpan.FromMilliseconds(120), new QuadraticEase { EasingMode = EasingMode.EaseIn }, Close);
    }

    private void Animate(double to, TimeSpan duration, IEasingFunction easing, Action? done = null)
    {
        // Windows' "Animation effects" off: no motion, as Orbit's Reduce Motion.
        if (!SystemParameters.ClientAreaAnimation)
        {
            Appear = to;
            done?.Invoke();
            return;
        }
        var animation = new DoubleAnimation(to, duration) { EasingFunction = easing };
        if (done is not null) animation.Completed += (_, _) => done();
        BeginAnimation(AppearProperty, animation);
    }

    /// <summary>The open/close animation: the ring grows and the items fly out from the hub.</summary>
    private void Layout()
    {
        double a = Appear;
        _rootScale.ScaleX = _rootScale.ScaleY = 0.86 + 0.14 * a;
        for (int i = 0; i < _items.Length; i++)
        {
            var (ox, oy) = Offset(i * 2 * Math.PI / _items.Length, RingTracker.ItemRadius);
            _items[i].Position(Center + ox * a, Center + oy * a, 0.4 + 0.6 * Math.Clamp(a, 0, 1.2), Math.Clamp(a, 0, 1));
        }
    }

    private void Refresh()
    {
        if (_tracker.PointerAngle is { } angle)
        {
            var (sx, sy) = Offset(angle, RingTracker.ItemRadius);
            Canvas.SetLeft(_satellite, Center + sx - _satellite.Width / 2);
            Canvas.SetTop(_satellite, Center + sy - _satellite.Height / 2);
            _satellite.Visibility = Visibility.Visible;
        }
        else _satellite.Visibility = Visibility.Collapsed;

        var selection = _tracker.Selection;
        for (int i = 0; i < _items.Length; i++) _items[i].Dwell(selection == i ? _tracker.DwellProgress : 0);
        if (selection == _shownSelection) return;
        _shownSelection = selection;
        for (int i = 0; i < _items.Length; i++) _items[i].Select(selection == i);

        if (selection is { } index)
        {
            double step = 2 * Math.PI / _items.Length, mid = index * step;
            _sector.Data = Sector(mid - step / 2, mid + step / 2, 40, Size / 2 - 20);
            _sector.Visibility = Visibility.Visible;
            var action = _slots[index];
            var text = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = action.ShortTitle(), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxHeight = 34 });
            text.Children.Add(new TextBlock { Text = action.GroupTitle(), FontSize = 10, Foreground = _palette.InkSecondary, TextAlignment = TextAlignment.Center });
            _hub.Content = text;
        }
        else
        {
            _sector.Visibility = Visibility.Collapsed;
            _hub.Content = _mark;
        }
    }

    /// <summary>An annular sector between two angles (0 = up, clockwise).</summary>
    private static Geometry Sector(double start, double end, double inner, double outer)
    {
        Point At(double angle, double radius)
        {
            var (x, y) = Offset(angle, radius);
            return new Point(Center + x, Center + y);
        }
        bool large = end - start > Math.PI;
        var figure = new PathFigure { StartPoint = At(start, outer), IsClosed = true };
        figure.Segments.Add(new ArcSegment(At(end, outer), new System.Windows.Size(outer, outer), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(At(end, inner), true));
        figure.Segments.Add(new ArcSegment(At(start, inner), new System.Windows.Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>One action on the ring: a disc with its icon; accent when chosen; the dwell's arc.</summary>
    private sealed class Item
    {
        private readonly Ellipse _disc;
        private readonly TextBlock _glyph;
        private readonly Path _arc;
        private readonly ScaleTransform _scale = new(1, 1);
        private readonly Palette _palette;
        private readonly double _size;
        private double _appearScale = 1;
        private bool _selected;

        public Item(OrbitAction action, double scale, Palette palette)
        {
            _palette = palette;
            _size = 60 * scale;
            _disc = new Ellipse { Width = 52 * scale, Height = 52 * scale, Fill = palette.Surface, Stroke = palette.Hairline, StrokeThickness = 1 };
            _glyph = new TextBlock
            {
                Text = Glyphs.Of(action), FontFamily = Glyphs.Font, FontSize = 19 * scale, Foreground = palette.Ink,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            _arc = new Path { Stroke = Brushes.White, StrokeThickness = 2.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            Root = new Grid
            {
                Width = _size, Height = _size, Background = Brushes.Transparent, RenderTransform = _scale,
                RenderTransformOrigin = new Point(0.5, 0.5), ToolTip = action.Title(), Cursor = Cursors.Hand,
            };
            Root.Children.Add(_disc);
            Root.Children.Add(_glyph);
            Root.Children.Add(_arc);
            _disc.HorizontalAlignment = HorizontalAlignment.Center;
            _disc.VerticalAlignment = VerticalAlignment.Center;
        }

        public Grid Root { get; }

        public void Position(double cx, double cy, double appearScale, double opacity)
        {
            Canvas.SetLeft(Root, cx - _size / 2);
            Canvas.SetTop(Root, cy - _size / 2);
            Root.Opacity = opacity;
            _appearScale = appearScale;
            ApplyScale();
        }

        public void Select(bool selected)
        {
            _selected = selected;
            _disc.Fill = selected ? _palette.Accent : _palette.Surface;
            _disc.Stroke = selected ? Brushes.Transparent : _palette.Hairline;
            _glyph.Foreground = selected ? Brushes.White : _palette.Ink;
            Root.Effect = selected ? new DropShadowEffect { Color = _palette.Accent.Color, BlurRadius = 20, ShadowDepth = 4, Direction = 270, Opacity = 0.45 } : null;
            ApplyScale();
        }

        public void Dwell(double progress)
        {
            if (progress <= 0) { _arc.Data = null; return; }
            double r = _size / 2 - 1.5, c = _size / 2;
            double angle = Math.Min(progress, 0.9999) * 2 * Math.PI;
            var end = new Point(c + Math.Sin(angle) * r, c - Math.Cos(angle) * r);
            var figure = new PathFigure { StartPoint = new Point(c, c - r) };
            figure.Segments.Add(new ArcSegment(end, new System.Windows.Size(r, r), 0, angle > Math.PI, SweepDirection.Clockwise, true));
            _arc.Data = new PathGeometry([figure]);
        }

        private void ApplyScale() => _scale.ScaleX = _scale.ScaleY = _appearScale * (_selected ? 1.16 : 1);
    }
}
