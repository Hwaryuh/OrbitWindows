using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Orbit.App.Design;

namespace Orbit.App.Features.Ring;

/// <summary>Geometry the ring and its pictures in Settings share: angle 0 is up, clockwise, y down.</summary>
internal static class RingDrawing
{
    public static (double X, double Y) Offset(double angle, double radius) => (Math.Sin(angle) * radius, -Math.Cos(angle) * radius);

    /// <summary>Puts an element with a set size on a canvas, centered on a point.</summary>
    public static void Place(Canvas canvas, FrameworkElement element, double cx, double cy)
    {
        Canvas.SetLeft(element, cx - element.Width / 2);
        Canvas.SetTop(element, cy - (double.IsNaN(element.Height) ? 0 : element.Height / 2));
        canvas.Children.Add(element);
    }

    /// <summary>Faint lines on the boundaries between slots, from <paramref name="inner"/> to <paramref name="outer"/>, around a center.</summary>
    public static Path Spokes(int count, double center, double inner, double outer, Brush stroke)
    {
        var geometry = new StreamGeometry();
        if (count > 1)
        {
            using var g = geometry.Open();
            double step = 2 * Math.PI / count;
            for (int i = 0; i < count; i++)
            {
                double a = (i + 0.5) * step;
                var (ix, iy) = Offset(a, inner);
                var (ox, oy) = Offset(a, outer);
                g.BeginFigure(new Point(center + ix, center + iy), false, false);
                g.LineTo(new Point(center + ox, center + oy), true, false);
            }
        }
        geometry.Freeze();
        return new Path { Data = geometry, Stroke = stroke, StrokeThickness = 1, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    }

    /// <summary>The faint stroke the spokes use.</summary>
    public static Brush SpokeStroke(Palette palette) => Palette.Fade(palette.Ink, 0.07);
}
