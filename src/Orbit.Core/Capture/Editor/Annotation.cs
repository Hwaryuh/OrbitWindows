namespace Orbit.Core.Capture.Editor;

/// <summary>A point in the editor's picture, in points (image pixels × 96 / the capture's DPI), y down.</summary>
public readonly record struct PointD(double X, double Y)
{
    public double DistanceTo(PointD other) => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));
}

/// <summary>The editor's tools (Orbit: <c>Annotation.Tool</c>).</summary>
public enum EditorTool
{
    Select, Arrow, Line, Rect, Ellipse, Pen, Highlight, Text, Counter,
    Blur, Pixelate, Erase, Spotlight, Loupe, Measure, Picker, Crop,
}

/// <summary>The toolbar's groups: one button each, the rest of a group a click away.</summary>
public enum ToolGroup { Select, Shapes, Draw, Text, Counter, Redact, Inspect, Crop }

public enum TextStyle { Shadow, Plain, Pill, Outline }

public enum TextWeight { Regular, Semibold, Bold, Heavy }

public static class EditorTools
{
    /// <summary>Single-letter keys, Shottr-style, as Orbit.</summary>
    public static char Key(this EditorTool tool) => tool switch
    {
        EditorTool.Select => 'V',
        EditorTool.Arrow => 'A',
        EditorTool.Line => 'I',
        EditorTool.Rect => 'R',
        EditorTool.Ellipse => 'O',
        EditorTool.Pen => 'D',
        EditorTool.Highlight => 'H',
        EditorTool.Text => 'T',
        EditorTool.Counter => 'N',
        EditorTool.Blur => 'B',
        EditorTool.Pixelate => 'P',
        EditorTool.Erase => 'E',
        EditorTool.Spotlight => 'S',
        EditorTool.Loupe => 'G',
        EditorTool.Measure => 'M',
        EditorTool.Picker => 'K',
        _ => 'C',
    };

    public static EditorTool? ForKey(char key) =>
        Enum.GetValues<EditorTool>().Cast<EditorTool?>().FirstOrDefault(t => t!.Value.Key() == char.ToUpperInvariant(key));

    /// <summary>Tools that hide what's under them; drawn before the rest.</summary>
    public static bool Redacts(this EditorTool tool) => tool is EditorTool.Blur or EditorTool.Pixelate or EditorTool.Erase;

    public static IReadOnlyList<EditorTool> Tools(this ToolGroup group) => group switch
    {
        ToolGroup.Select => [EditorTool.Select],
        ToolGroup.Shapes => [EditorTool.Arrow, EditorTool.Line, EditorTool.Rect, EditorTool.Ellipse],
        ToolGroup.Draw => [EditorTool.Pen, EditorTool.Highlight],
        ToolGroup.Text => [EditorTool.Text],
        ToolGroup.Counter => [EditorTool.Counter],
        ToolGroup.Redact => [EditorTool.Blur, EditorTool.Pixelate, EditorTool.Erase, EditorTool.Spotlight],
        ToolGroup.Inspect => [EditorTool.Loupe, EditorTool.Measure, EditorTool.Picker],
        _ => [EditorTool.Crop],
    };

    public static ToolGroup Group(this EditorTool tool) =>
        Enum.GetValues<ToolGroup>().First(g => g.Tools().Contains(tool));

    /// <summary>
    /// The pen colors (Orbit: <c>EditorModel.palette</c>) as ARGB: red, orange, yellow, green, blue,
    /// purple, white, black. Keys 1–8 pick them.
    /// </summary>
    public static readonly uint[] Palette = [0xFFFF4554, 0xFFFF9E1A, 0xFFFFD61A, 0xFF33CC73, 0xFF3880FF, 0xFF9E66FF, 0xFFFFFFFF, 0xFF000000];

    /// <summary>Black text on light colors, white on dark ones (Orbit: <c>contrast</c>).</summary>
    public static uint Contrast(uint argb)
    {
        double r = ((argb >> 16) & 0xFF) / 255.0, g = ((argb >> 8) & 0xFF) / 255.0, b = (argb & 0xFF) / 255.0;
        return 0.299 * r + 0.587 * g + 0.114 * b > 0.7 ? 0xFF000000 : 0xFFFFFFFF;
    }

    public static string Hex(uint argb) => $"#{(argb >> 16) & 0xFF:X2}{(argb >> 8) & 0xFF:X2}{argb & 0xFF:X2}";
}

/// <summary>
/// One mark on the picture (Orbit: <c>Annotation</c>). Immutable: an edit makes a new one with
/// the same <see cref="Id"/>, so the undo history shares everything that did not change.
/// </summary>
public sealed record Annotation
{
    public const double LoupeZoom = 2.5;

    public Guid Id { get; init; } = Guid.NewGuid();
    public required EditorTool Tool { get; init; }
    public required IReadOnlyList<PointD> Points { get; init; }
    public uint Color { get; init; } = EditorTools.Palette[0];
    public double Width { get; init; } = 4;
    public string Text { get; init; } = "";
    public int Number { get; init; }
    public bool Filled { get; init; }
    public double CornerRadius { get; init; } = 3;
    public bool Curved { get; init; }
    public double FontSize { get; init; } = 24;
    public TextWeight Weight { get; init; } = TextWeight.Bold;
    public TextStyle TextStyle { get; init; } = TextStyle.Shadow;
    /// <summary>Blur and pixelate strength, 1–10.</summary>
    public double Strength { get; init; } = 5;
    /// <summary>The erase tool's fill, sampled from around the area when it's drawn.</summary>
    public uint? Fill { get; init; }

    public PointD Start => Points.Count > 0 ? Points[0] : default;
    public PointD End => Points.Count > 0 ? Points[^1] : default;

    public Area Rect => new(Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y), Math.Abs(End.X - Start.X), Math.Abs(End.Y - Start.Y));

    public double CounterDiameter => 18 + Width * 3;
    public double LoupeRadius => 36 + Width * 7;

    /// <summary>Where the magnifier's lens sits: where the drag ended, or up and to the right for a click.</summary>
    public PointD LoupeCenter => Start.DistanceTo(End) < 6 ? new PointD(Start.X + LoupeRadius * 1.6, Start.Y - LoupeRadius * 1.6) : End;

    /// <summary>Approximate bounds for hit testing; text needs its measured size.</summary>
    public Area Bounds((double Width, double Height) textSize)
    {
        switch (Tool)
        {
            case EditorTool.Text:
                double pad = TextStyle == TextStyle.Pill ? FontSize * 0.4 : 0;
                return new Area(Start.X - pad, Start.Y - pad / 2, textSize.Width + pad * 2, textSize.Height + pad);
            case EditorTool.Counter:
                double d = CounterDiameter;
                return new Area(Start.X - d / 2, Start.Y - d / 2, d, d);
            case EditorTool.Loupe:
                double r = LoupeRadius;
                var c = LoupeCenter;
                return new Area(c.X - r, c.Y - r, r * 2, r * 2);
            case EditorTool.Pen or EditorTool.Highlight:
                double minX = Points.Min(p => p.X), minY = Points.Min(p => p.Y);
                return new Area(minX, minY, Points.Max(p => p.X) - minX, Points.Max(p => p.Y) - minY);
            default:
                return Rect;
        }
    }

    public Annotation Translated(double dx, double dy) => this with { Points = Points.Select(p => new PointD(p.X + dx, p.Y + dy)).ToArray() };
}
