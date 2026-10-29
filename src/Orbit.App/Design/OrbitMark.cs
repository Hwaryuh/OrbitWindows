using System.Windows;
using System.Windows.Media;

namespace Orbit.App.Design;

/// <summary>
/// Orbit's glyph (Modules/Ring/OrbitRing.swift, <c>OrbitMark</c>): a planet, a tilted ring with
/// knocked-out gaps where it passes in front, and a satellite. Monochrome in <see cref="Ink"/>;
/// with <see cref="Dot"/> the satellite takes <see cref="Accent"/>. Geometry in a unit square,
/// as Orbit's 1024-pt master.
/// </summary>
internal sealed class OrbitMark : FrameworkElement
{
    private const double Tilt = -0.38;
    private const double A = 376.0 / 1024, B = 116.0 / 1024, RingWidth = 46.0 / 1024, Gap = 26.0 / 1024;
    private const double Planet = 490.0 / 1024, DotDiameter = 92.0 / 1024, DotGap = 30.0 / 1024, DotT = 2.45;

    public double Size { get; init; } = 18;
    public Brush Ink { get; init; } = Brushes.Black;
    public Brush Accent { get; init; } = Brushes.OrangeRed;
    public bool Dot { get; init; }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        double u = Size;
        var c = new Point(u / 2, u / 2);
        var tilt = new RotateTransform(Tilt * 180 / Math.PI, c.X, c.Y);
        double ringW = Math.Max(1.2, RingWidth * u), gapW = Math.Max(0.8, Gap * u);
        var ringPen = new Pen(Brushes.Black, ringW);

        Geometry Arc(double from, double to) => Ellipse(c, A * u, B * u, from, to, tilt);
        Geometry Circle(Point p, double r) => new EllipseGeometry(p, r, r);

        double pr = Planet * u / 2;
        // The whole ring, with a gap cut around the planet; the planet; then the front of the ring
        // cut through the planet and drawn again on top.
        Geometry shape = Arc(0, 1).GetWidenedPathGeometry(ringPen);
        shape = Geometry.Combine(shape, Circle(c, pr + gapW), GeometryCombineMode.Exclude, null);
        shape = Geometry.Combine(shape, Circle(c, pr), GeometryCombineMode.Union, null);
        shape = Geometry.Combine(shape, Arc(0.06, 0.44).GetWidenedPathGeometry(new Pen(Brushes.Black, ringW + gapW * 2)), GeometryCombineMode.Exclude, null);
        shape = Geometry.Combine(shape, Arc(0, 0.5).GetWidenedPathGeometry(ringPen), GeometryCombineMode.Union, null);

        var dot = DotPoint(u, c);
        double dr = DotDiameter * u / 2, dg = DotGap * u;
        shape = Geometry.Combine(shape, Circle(dot, dr + dg), GeometryCombineMode.Exclude, null);
        dc.DrawGeometry(Ink, null, shape);
        dc.DrawGeometry(Dot ? Accent : Ink, null, Circle(dot, dr));
    }

    /// <summary>A stretch of the ellipse, as a fraction of its turn from its rightmost point, y down.</summary>
    private static Geometry Ellipse(Point c, double a, double b, double from, double to, Transform tilt)
    {
        const int steps = 96;
        var figure = new PathFigure { IsClosed = from == 0 && to == 1, IsFilled = false };
        for (int i = 0; i <= steps; i++)
        {
            double t = (from + (to - from) * i / steps) * 2 * Math.PI;
            var p = new Point(c.X + a * Math.Cos(t), c.Y + b * Math.Sin(t));
            if (i == 0) figure.StartPoint = p;
            else figure.Segments.Add(new LineSegment(p, true));
        }
        var geometry = new PathGeometry([figure]) { Transform = tilt };
        return geometry;
    }

    private static Point DotPoint(double u, Point c)
    {
        double x = A * u * Math.Cos(DotT), y = B * u * Math.Sin(DotT);
        return new Point(x * Math.Cos(Tilt) - y * Math.Sin(Tilt) + c.X, x * Math.Sin(Tilt) + y * Math.Cos(Tilt) + c.Y);
    }
}
