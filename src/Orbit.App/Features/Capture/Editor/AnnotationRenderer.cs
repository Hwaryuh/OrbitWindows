using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbit.App.Design;
using Orbit.Core.Capture;
using Orbit.Core.Capture.Editor;

namespace Orbit.App.Features.Capture.Editor;

/// <summary>
/// Draws the picture and its marks (Orbit: AnnotatedImage.swift), in points, on screen and for
/// export. WPF has no per-draw shadow filter or multiply blend: shadows are a soft dark copy one
/// point lower, and the highlighter is its color at 45 % over the picture.
/// </summary>
internal sealed class AnnotationRenderer(EditorModel model, BitmapSource image, RedactionCache redactions)
{
    private static readonly FontFamily Family = new("Segoe UI Variable Display, Segoe UI, Malgun Gothic");
    private static readonly Brush Spot = Palette.Fade(Brushes.Black, 0.55);
    private static readonly Brush ShadowBrush = Palette.Fade(Brushes.Black, 0.3);
    private static readonly Brush Placeholder = new SolidColorBrush(Color.FromRgb(120, 120, 120)).Frozen();

    private bool _exporting;
    private IReadOnlyList<Annotation> _hiding = [];

    public EditorModel Model { get; } = model;
    public BitmapSource Image { get; } = image;

    /// <param name="scale">Screen DIPs per point, so the editor's own guides keep their size; 1 for export.</param>
    public void Draw(DrawingContext dc, double scale, bool exporting)
    {
        _exporting = exporting;
        var full = new Rect(0, 0, Model.Width, Model.Height);
        dc.DrawImage(Image, full);

        var all = Model.Annotations.ToList();
        if (Model.Draft is { Tool: not EditorTool.Crop } draft) all.Add(draft);

        // Hiding first, then the dimming around spotlights, then marks on top so they stay bright.
        var hiding = all.Where(a => a.Tool.Redacts()).ToList();
        _hiding = hiding;
        foreach (var a in hiding) DrawMark(dc, a, full);
        var spots = all.Where(a => a.Tool == EditorTool.Spotlight).ToList();
        if (spots.Count > 0)
        {
            var outside = new GeometryGroup { FillRule = FillRule.EvenOdd };
            outside.Children.Add(new RectangleGeometry(full));
            foreach (var s in spots) outside.Children.Add(new RectangleGeometry(ToRect(s.Rect), 8, 8));
            dc.DrawGeometry(Spot, null, outside);
        }
        foreach (var a in all.Where(a => !a.Tool.Redacts() && a.Tool != EditorTool.Spotlight)) DrawMark(dc, a, full);

        if (exporting) return;
        if (Model.Selected is { } selected)
        {
            var b = ToRect(Model.Bounds(selected));
            b.Inflate(6, 6);
            dc.DrawRoundedRectangle(null, Dashed(Palette.DarkTheme.Accent, 1.5 / scale, 4 / scale, 3 / scale), b, 4, 4);
        }
        if (Model.Draft is { Tool: EditorTool.Spotlight } spot)
            dc.DrawRoundedRectangle(null, Dashed(Palette.Fade(Brushes.White, 0.8), 1 / scale, 5 / scale, 4 / scale), ToRect(spot.Rect), 8, 8);
        var crop = Model.Draft is { Tool: EditorTool.Crop } c ? c.Rect : Model.Crop;
        if (crop is { } cropArea)
        {
            var cropRect = ToRect(cropArea);
            var outside = new GeometryGroup { FillRule = FillRule.EvenOdd };
            outside.Children.Add(new RectangleGeometry(full));
            outside.Children.Add(new RectangleGeometry(cropRect));
            dc.DrawGeometry(Spot, null, outside);
            dc.DrawRectangle(null, Dashed(Brushes.White, 1.5 / scale, 6 / scale, 4 / scale), cropRect);
            var (_, _, pw, ph) = AnnotationGeometry.PixelCrop(cropArea, Model.PixelScale, Image.PixelWidth, Image.PixelHeight);
            var label = Text($"{pw} × {ph}", 11 / scale, FontWeights.SemiBold, Brushes.White);
            dc.DrawText(label, new Point(cropRect.X + cropRect.Width / 2 - label.Width / 2, cropRect.Bottom + 6 / scale));
        }
    }

    private void DrawMark(DrawingContext dc, Annotation a, Rect full)
    {
        var color = ToBrush(a.Color);
        switch (a.Tool)
        {
            case EditorTool.Arrow:
            {
                var shape = AnnotationGeometry.Arrow(a.Start, a.End, a.Width, a.Curved);
                var shaft = new StreamGeometry();
                using (var g = shaft.Open())
                {
                    g.BeginFigure(P(shape.Start), false, false);
                    if (shape.Control is { } control) g.QuadraticBezierTo(P(control), P(shape.Base), true, true);
                    else g.LineTo(P(shape.Base), true, true);
                }
                var head = new StreamGeometry();
                using (var g = head.Open())
                {
                    g.BeginFigure(P(shape.Tip), true, true);
                    g.LineTo(P(shape.Left), true, true);
                    g.QuadraticBezierTo(P(shape.Base), P(shape.Right), true, true);
                }
                var pen = RoundPen(color, a.Width);
                Shadowed(dc, d =>
                {
                    d.DrawGeometry(null, pen, shaft);
                    d.DrawGeometry(color, null, head);
                }, a.Width);
                break;
            }
            case EditorTool.Line:
            {
                var pen = RoundPen(color, a.Width);
                Shadowed(dc, d => d.DrawLine(pen, P(a.Start), P(a.End)), a.Width);
                break;
            }
            case EditorTool.Rect:
            {
                var r = ToRect(a.Rect);
                double radius = Math.Min(a.CornerRadius, Math.Min(r.Width, r.Height) / 2);
                Shadowed(dc, d => d.DrawRoundedRectangle(a.Filled ? color : null, a.Filled ? null : new Pen(color, a.Width), r, radius, radius), a.Width);
                break;
            }
            case EditorTool.Ellipse:
            {
                var r = ToRect(a.Rect);
                var center = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
                Shadowed(dc, d => d.DrawEllipse(a.Filled ? color : null, a.Filled ? null : new Pen(color, a.Width), center, r.Width / 2, r.Height / 2), a.Width);
                break;
            }
            case EditorTool.Pen:
            {
                var path = Smooth(a.Points);
                var pen = RoundPen(color, a.Width);
                Shadowed(dc, d => d.DrawGeometry(null, pen, path), a.Width);
                break;
            }
            case EditorTool.Highlight:
                dc.DrawGeometry(null, RoundPen(ToBrush(a.Color, 0.45), a.Width * 5 + 8), Smooth(a.Points));
                break;
            case EditorTool.Text:
                DrawText(dc, a);
                break;
            case EditorTool.Counter:
            {
                double d = a.CounterDiameter;
                var center = P(a.Start);
                Shadowed(dc, x => x.DrawEllipse(color, null, center, d / 2, d / 2), 2);
                dc.DrawEllipse(null, new Pen(Palette.Fade(Brushes.White, 0.9), 2), center, d / 2 - 1, d / 2 - 1);
                var number = Text(a.Number.ToString(CultureInfo.InvariantCulture), d * 0.52, FontWeights.Bold, ToBrush(EditorTools.Contrast(a.Color)));
                dc.DrawText(number, new Point(center.X - number.Width / 2, center.Y - number.Height / 2));
                break;
            }
            case EditorTool.Blur or EditorTool.Pixelate:
            {
                var r = ToRect(a.Rect);
                double radius = a.Tool == EditorTool.Blur ? 6 : 2;
                int level = (int)Math.Round(a.Strength);
                if ((_exporting ? redactions.GetNow(a.Tool, level) : redactions.Get(a.Tool, level)) is { } hidden)
                {
                    dc.PushClip(new RectangleGeometry(r, radius, radius));
                    dc.DrawImage(hidden, full);
                    dc.Pop();
                }
                else
                {
                    // Still being made: never show the clear picture where it should be hidden.
                    dc.DrawRoundedRectangle(Placeholder, null, r, radius, radius);
                }
                break;
            }
            case EditorTool.Erase:
                dc.DrawRectangle(ToBrush(a.Fill ?? 0xFFFFFFFF), null, ToRect(a.Rect));
                break;
            case EditorTool.Loupe:
                DrawLoupe(dc, a, full);
                break;
            case EditorTool.Measure:
                DrawRuler(dc, a);
                break;
        }
    }

    private void DrawText(DrawingContext dc, Annotation a)
    {
        var weight = Weight(a.Weight);
        var origin = P(a.Start);
        switch (a.TextStyle)
        {
            case TextStyle.Shadow:
                dc.DrawText(Text(a.Text, a.FontSize, weight, Palette.Fade(Brushes.Black, 0.45)), origin + new Vector(0, 1));
                dc.DrawText(Text(a.Text, a.FontSize, weight, ToBrush(a.Color)), origin);
                break;
            case TextStyle.Plain:
                dc.DrawText(Text(a.Text, a.FontSize, weight, ToBrush(a.Color)), origin);
                break;
            case TextStyle.Pill:
            {
                var b = ToRect(Model.Bounds(a));
                double radius = Math.Min(b.Height / 2, a.FontSize * 0.7);
                dc.DrawRoundedRectangle(ShadowBrush, null, new Rect(b.X, b.Y + 1, b.Width, b.Height), radius, radius);
                dc.DrawRoundedRectangle(ToBrush(a.Color), null, b, radius, radius);
                dc.DrawText(Text(a.Text, a.FontSize, weight, ToBrush(EditorTools.Contrast(a.Color))), origin);
                break;
            }
            case TextStyle.Outline:
            {
                var edge = ToBrush(EditorTools.Contrast(a.Color));
                double o = Math.Max(1, a.FontSize / 14);
                foreach (var (dx, dy) in new[] { (-o, 0.0), (o, 0.0), (0.0, -o), (0.0, o), (-o, -o), (o, o), (-o, o), (o, -o) })
                    dc.DrawText(Text(a.Text, a.FontSize, weight, edge), origin + new Vector(dx, dy));
                dc.DrawText(Text(a.Text, a.FontSize, weight, ToBrush(a.Color)), origin);
                break;
            }
        }
    }

    private void DrawLoupe(DrawingContext dc, Annotation a, Rect full)
    {
        double r = a.LoupeRadius, z = Annotation.LoupeZoom;
        var c = P(a.LoupeCenter);
        var s = P(a.Start);
        // A thin leader from the spot to the lens, and a ring on the spot itself.
        double spot = r / z;
        double angle = Math.Atan2(c.Y - s.Y, c.X - s.X);
        var ring = new Pen(Palette.Fade(Brushes.White, 0.9), 1.5);
        dc.DrawLine(ring, new Point(s.X + Math.Cos(angle) * spot, s.Y + Math.Sin(angle) * spot), new Point(c.X - Math.Cos(angle) * r, c.Y - Math.Sin(angle) * r));
        dc.DrawEllipse(null, ring, s, spot, spot);
        dc.DrawEllipse(Palette.Fade(Brushes.Black, 0.25), null, c + new Vector(0, 3), r + 2, r + 2);
        dc.DrawEllipse(Brushes.White, null, c, r, r);
        dc.PushClip(new EllipseGeometry(c, r, r));
        // The picture as it shows, hidden areas included: the lens must not reveal what blur hides.
        dc.PushTransform(new MatrixTransform(z, 0, 0, z, c.X - s.X * z, c.Y - s.Y * z));
        dc.DrawImage(Image, full);
        foreach (var hidden in _hiding) DrawMark(dc, hidden, full);
        dc.Pop();
        dc.Pop();
        dc.DrawEllipse(null, new Pen(ToBrush(a.Color), 3), c, r - 1.5, r - 1.5);
    }

    private void DrawRuler(DrawingContext dc, Annotation a)
    {
        var s = P(a.Start);
        var e = P(a.End);
        double angle = Math.Atan2(e.Y - s.Y, e.X - s.X), tick = 6;
        var path = new StreamGeometry();
        using (var g = path.Open())
        {
            g.BeginFigure(s, false, false);
            g.LineTo(e, true, true);
            foreach (var p in new[] { s, e })
            {
                g.BeginFigure(new Point(p.X + Math.Cos(angle + Math.PI / 2) * tick, p.Y + Math.Sin(angle + Math.PI / 2) * tick), false, false);
                g.LineTo(new Point(p.X - Math.Cos(angle + Math.PI / 2) * tick, p.Y - Math.Sin(angle + Math.PI / 2) * tick), true, true);
            }
        }
        dc.DrawGeometry(null, RoundPen(Palette.Fade(Brushes.Black, 0.5), 3.5), path);
        dc.DrawGeometry(null, RoundPen(ToBrush(a.Color), 1.5), path);
        var label = Text(AnnotationGeometry.RulerLabel(a.Start, a.End, Model.PixelScale), 11, FontWeights.SemiBold, Brushes.White);
        var mid = new Point((s.X + e.X) / 2, (s.Y + e.Y) / 2);
        var pill = new Rect(mid.X - label.Width / 2 - 7, mid.Y - label.Height / 2 - 3, label.Width + 14, label.Height + 6);
        dc.DrawRoundedRectangle(Palette.Fade(Brushes.Black, 0.75), null, pill, pill.Height / 2, pill.Height / 2);
        dc.DrawText(label, new Point(mid.X - label.Width / 2, mid.Y - label.Height / 2));
    }

    // MARK: Pieces

    /// <summary>The drawing, with a soft shadow under it (Orbit: a 2.5-point shadow one point down).</summary>
    private static void Shadowed(DrawingContext dc, Action<DrawingContext> draw, double width)
    {
        dc.PushTransform(new TranslateTransform(0, Math.Max(1, width * 0.15)));
        dc.PushOpacity(0.28);
        draw(dc);
        dc.Pop();
        dc.Pop();
        draw(dc);
    }

    /// <summary>Text as the editor draws and measures it.</summary>
    public static FormattedText Text(string text, double size, FontWeight weight, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(Family, FontStyles.Normal, weight, FontStretches.Normal),
            Math.Max(1, size), brush, 1);

    /// <summary>A text mark's size, as Orbit's <c>textSize</c>: at least 20 wide and one line tall.</summary>
    public static (double Width, double Height) Measure(Annotation a)
    {
        var t = Text(a.Text, a.FontSize, Weight(a.Weight), Brushes.Black);
        return (Math.Max(20, Math.Ceiling(t.WidthIncludingTrailingWhitespace)), Math.Max(a.FontSize * 1.2, Math.Ceiling(t.Height)));
    }

    public static FontWeight Weight(TextWeight weight) => weight switch
    {
        TextWeight.Regular => FontWeights.Normal,
        TextWeight.Semibold => FontWeights.SemiBold,
        TextWeight.Heavy => FontWeights.Black,
        _ => FontWeights.Bold,
    };

    private static Geometry Smooth(IReadOnlyList<PointD> points)
    {
        var path = new StreamGeometry();
        using (var g = path.Open())
        {
            g.BeginFigure(P(points[0]), false, false);
            foreach (var (control, end) in AnnotationGeometry.Smooth(points)) g.QuadraticBezierTo(P(control), P(end), true, true);
        }
        path.Freeze();
        return path;
    }

    private static Pen RoundPen(Brush brush, double width) =>
        new(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

    private static Pen Dashed(Brush brush, double width, double dash, double gap) =>
        new(brush, width) { DashStyle = new DashStyle([dash / width, gap / width], 0) };

    public static SolidColorBrush ToBrush(uint argb, double opacity = 1) =>
        new SolidColorBrush(Color.FromArgb((byte)(((argb >> 24) & 0xFF) * opacity), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb)).Frozen();

    private static Point P(PointD p) => new(p.X, p.Y);

    private static Rect ToRect(Area a) => new(a.X, a.Y, Math.Max(0, a.Width), Math.Max(0, a.Height));
}
