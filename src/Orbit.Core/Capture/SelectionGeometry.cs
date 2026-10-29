namespace Orbit.Core.Capture;

/// <summary>Aspect presets for the area selection, cycled with Tab / Shift+Tab.</summary>
public enum AspectPreset { Free, Square, FourThree, ThreeTwo, SixteenNine, NineSixteen }

public static class AspectPresets
{
    /// <summary>Width over height; null is free.</summary>
    public static double? Ratio(this AspectPreset preset) => preset switch
    {
        AspectPreset.Square => 1,
        AspectPreset.FourThree => 4.0 / 3,
        AspectPreset.ThreeTwo => 3.0 / 2,
        AspectPreset.SixteenNine => 16.0 / 9,
        AspectPreset.NineSixteen => 9.0 / 16,
        _ => null,
    };

    public static string Label(this AspectPreset preset) => preset switch
    {
        AspectPreset.Square => "1:1",
        AspectPreset.FourThree => "4:3",
        AspectPreset.ThreeTwo => "3:2",
        AspectPreset.SixteenNine => "16:9",
        AspectPreset.NineSixteen => "9:16",
        _ => "",
    };

    public static AspectPreset Next(this AspectPreset preset, bool backward = false)
    {
        int count = Enum.GetValues<AspectPreset>().Length;
        return (AspectPreset)(((int)preset + (backward ? count - 1 : 1)) % count);
    }
}

/// <summary>A rectangle in any unit, y down.</summary>
public readonly record struct Area(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;

    public Area Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };
}

/// <summary>
/// The math behind a drag (Orbit: SelectionGeometry.swift): where it started, where the pointer is,
/// and arrow-key nudges of the moving corner. Pure, so it can be checked without a screen. Orbit
/// works in points with y up; this works in the overlay's physical pixels with y down.
/// </summary>
public sealed class SelectionGeometry(double startX, double startY)
{
    public double AnchorX { get; private set; } = startX;
    public double AnchorY { get; private set; } = startY;
    public double PointerX { get; private set; } = startX;
    public double PointerY { get; private set; } = startY;
    private double _offsetX, _offsetY;

    /// <summary>The corner that follows the pointer.</summary>
    public (double X, double Y) Tip => (PointerX + _offsetX, PointerY + _offsetY);

    /// <summary>
    /// The selection, kept inside <paramref name="bounds"/>. With an aspect (width / height) it grows
    /// to hold the tip and shrinks as a whole at an edge, so the ratio holds; from the center, the
    /// anchor is the middle.
    /// </summary>
    public Area Rect(double? aspect, bool fromCenter, Area bounds)
    {
        var (tx, ty) = Tip;
        double dx = tx - AnchorX, dy = ty - AnchorY;
        double sx = dx < 0 ? -1 : 1, sy = dy < 0 ? -1 : 1;
        double w = Math.Abs(dx), h = Math.Abs(dy);
        if (aspect is { } r && r > 0 && (w > 0 || h > 0))
        {
            if (h == 0 || w / h > r) h = w / r;
            else w = h * r;
        }
        double roomX = Math.Max(0, fromCenter ? Math.Min(AnchorX - bounds.X, bounds.Right - AnchorX)
                                              : sx > 0 ? bounds.Right - AnchorX : AnchorX - bounds.X);
        double roomY = Math.Max(0, fromCenter ? Math.Min(AnchorY - bounds.Y, bounds.Bottom - AnchorY)
                                              : sy > 0 ? bounds.Bottom - AnchorY : AnchorY - bounds.Y);
        if (aspect is not null && w > 0 && h > 0)
        {
            double s = Math.Min(1, Math.Min(roomX / w, roomY / h));
            w *= s;
            h *= s;
        }
        else
        {
            w = Math.Min(w, roomX);
            h = Math.Min(h, roomY);
        }
        if (fromCenter) return new Area(AnchorX - w, AnchorY - h, w * 2, h * 2);
        return new Area(sx > 0 ? AnchorX : AnchorX - w, sy > 0 ? AnchorY : AnchorY - h, w, h);
    }

    /// <summary>The pointer moved. Carrying (Space held), the whole selection travels with it, stopping at the edges.</summary>
    public void Move(double x, double y, bool carrying, double? aspect, bool fromCenter, Area bounds)
    {
        if (!carrying)
        {
            PointerX = x;
            PointerY = y;
            return;
        }
        double dx = x - PointerX, dy = y - PointerY;
        var (mx, my) = ClampedShift(Rect(aspect, fromCenter, bounds), dx, dy, bounds);
        AnchorX += mx;
        AnchorY += my;
        // The tip moves by the same amount, whatever the pointer did past an edge.
        _offsetX += mx - dx;
        _offsetY += my - dy;
        PointerX = x;
        PointerY = y;
    }

    /// <summary>Arrow keys: the moving corner by (dx, dy), kept inside <paramref name="bounds"/>.</summary>
    public void Nudge(double dx, double dy, Area bounds)
    {
        var (tx, ty) = Tip;
        double x = Math.Clamp(tx + dx, bounds.X, bounds.Right), y = Math.Clamp(ty + dy, bounds.Y, bounds.Bottom);
        _offsetX += x - tx;
        _offsetY += y - ty;
    }

    /// <summary>How far <paramref name="rect"/> can travel by (dx, dy) without leaving <paramref name="bounds"/>.</summary>
    public static (double X, double Y) ClampedShift(Area rect, double dx, double dy, Area bounds) =>
        (Math.Min(Math.Max(dx, bounds.X - rect.X), bounds.Right - rect.Right),
         Math.Min(Math.Max(dy, bounds.Y - rect.Y), bounds.Bottom - rect.Bottom));

    public static Area Shifted(Area rect, double dx, double dy, Area bounds)
    {
        var (x, y) = ClampedShift(rect, dx, dy, bounds);
        return rect.Offset(x, y);
    }

    /// <summary>"#RRGGBB".</summary>
    public static string Hex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
}
