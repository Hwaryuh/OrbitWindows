namespace Orbit.Core.Capture.Editor;

/// <summary>An arrow's parts: the shaft to <see cref="Base"/> (curved through <see cref="Control"/>), and the head.</summary>
public readonly record struct ArrowShape(PointD Start, PointD? Control, PointD Base, PointD Tip, PointD Left, PointD Right);

/// <summary>The shapes behind the marks (Orbit: AnnotatedImage.swift), as points for any drawing API.</summary>
public static class AnnotationGeometry
{
    /// <summary>Shift held: lines snap to 45°, boxes become squares.</summary>
    public static PointD Constrained(EditorTool tool, PointD s, PointD p)
    {
        double dx = p.X - s.X, dy = p.Y - s.Y;
        if (tool is EditorTool.Arrow or EditorTool.Line or EditorTool.Measure or EditorTool.Loupe)
        {
            double angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
            double length = Math.Sqrt(dx * dx + dy * dy);
            return new PointD(s.X + Math.Cos(angle) * length, s.Y + Math.Sin(angle) * length);
        }
        double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new PointD(s.X + side * (dx < 0 ? -1 : 1), s.Y + side * (dy < 0 ? -1 : 1));
    }

    /// <summary>A curved arrow bows to one side; its head follows the curve's direction at the tip.</summary>
    public static ArrowShape Arrow(PointD s, PointD e, double width, bool curved)
    {
        double length = s.DistanceTo(e);
        var control = new PointD((s.X + e.X) / 2 - (e.Y - s.Y) * 0.28, (s.Y + e.Y) / 2 + (e.X - s.X) * 0.28);
        double angle = curved ? Math.Atan2(e.Y - control.Y, e.X - control.X) : Math.Atan2(e.Y - s.Y, e.X - s.X);
        double headLength = Math.Min(Math.Max(14, width * 4.2), length * 0.6);
        double headWidth = headLength * 0.85;
        var bas = new PointD(e.X - Math.Cos(angle) * headLength * 0.8, e.Y - Math.Sin(angle) * headLength * 0.8);
        var back = new PointD(e.X - Math.Cos(angle) * headLength, e.Y - Math.Sin(angle) * headLength);
        var left = new PointD(back.X + Math.Cos(angle + Math.PI / 2) * headWidth / 2, back.Y + Math.Sin(angle + Math.PI / 2) * headWidth / 2);
        var right = new PointD(back.X + Math.Cos(angle - Math.PI / 2) * headWidth / 2, back.Y + Math.Sin(angle - Math.PI / 2) * headWidth / 2);
        return new ArrowShape(s, curved ? control : null, bas, e, left, right);
    }

    /// <summary>
    /// A freehand stroke smoothed into quadratic curves through the midpoints: (control, end) pairs
    /// after the first point, the last a straight line (control = end).
    /// </summary>
    public static IReadOnlyList<(PointD Control, PointD End)> Smooth(IReadOnlyList<PointD> points)
    {
        var segments = new List<(PointD, PointD)>();
        if (points.Count < 2) return segments;
        if (points.Count == 2)
        {
            segments.Add((points[1], points[1]));
            return segments;
        }
        for (int i = 1; i < points.Count - 1; i++)
        {
            var mid = new PointD((points[i].X + points[i + 1].X) / 2, (points[i].Y + points[i + 1].Y) / 2);
            segments.Add((points[i], mid));
        }
        segments.Add((points[^1], points[^1]));
        return segments;
    }

    /// <summary>The ruler's label: length in picture pixels, and both sides when it is slanted.</summary>
    public static string RulerLabel(PointD s, PointD e, double pixelScale)
    {
        int length = (int)Math.Round(s.DistanceTo(e) * pixelScale);
        int dx = (int)Math.Round(Math.Abs(e.X - s.X) * pixelScale), dy = (int)Math.Round(Math.Abs(e.Y - s.Y) * pixelScale);
        return dx > 2 && dy > 2 ? $"{length} px  ({dx} × {dy})" : $"{length} px";
    }

    /// <summary>A crop in points as whole picture pixels, inside the picture.</summary>
    public static (int X, int Y, int Width, int Height) PixelCrop(Area crop, double pixelScale, int pixelWidth, int pixelHeight)
    {
        int left = Math.Clamp((int)Math.Floor(crop.X * pixelScale), 0, pixelWidth);
        int top = Math.Clamp((int)Math.Floor(crop.Y * pixelScale), 0, pixelHeight);
        int right = Math.Clamp((int)Math.Ceiling(crop.Right * pixelScale), left, pixelWidth);
        int bottom = Math.Clamp((int)Math.Ceiling(crop.Bottom * pixelScale), top, pixelHeight);
        return (left, top, right - left, bottom - top);
    }
}
