using Orbit.Core.Display;

namespace Orbit.Core.Capture;

/// <summary>Which edge or corner of a window is being dragged to resize it (WM_SIZING's WMSZ_* values).</summary>
public enum SizingEdge { Left = 1, Right = 2, Top = 3, TopLeft = 4, TopRight = 5, Bottom = 6, BottomLeft = 7, BottomRight = 8 }

/// <summary>A picture pinned above everything (Orbit: PinWindow.swift). Physical pixels throughout.</summary>
public static class PinLayout
{
    /// <summary>The smallest a pin gets, in DIPs (Orbit's <c>minSize</c>).</summary>
    public const double MinSide = 60;
    public const double MinOpacity = 0.2;

    /// <summary>
    /// Where a new pin goes: the picture at one screen pixel per image pixel, scaled down until its
    /// longer side is at most 0.6 of the work area's shorter side, centered on the work area.
    /// </summary>
    public static PixelRect Initial(int width, int height, PixelRect work)
    {
        double w = Math.Max(1, width), h = Math.Max(1, height);
        double maxSide = Math.Min(work.Width, work.Height) * 0.6;
        double longer = Math.Max(w, h);
        if (longer > maxSide)
        {
            double f = maxSide / longer;
            w *= f;
            h *= f;
        }
        int pw = Math.Max(1, (int)Math.Round(w)), ph = Math.Max(1, (int)Math.Round(h));
        return new PixelRect(work.X + (work.Width - pw) / 2, work.Y + (work.Height - ph) / 2, pw, ph);
    }

    /// <summary>The opacity after a wheel turn of <paramref name="notches"/> (up is positive), 0.05 a notch.</summary>
    public static double Opacity(double current, double notches) => Math.Clamp(current + notches * 0.05, MinOpacity, 1);

    /// <summary>
    /// <paramref name="rect"/> grown or shrunk by <paramref name="factor"/> about its center, keeping
    /// the ratio, no smaller than <paramref name="minSide"/> on its shorter side and no larger than
    /// <paramref name="limit"/> (the virtual screen) on either side.
    /// </summary>
    public static PixelRect Zoom(PixelRect rect, double factor, int minSide, PixelRect limit)
    {
        if (rect.IsEmpty) return rect;
        double ratio = (double)rect.Width / rect.Height;
        double w = rect.Width * factor;
        double shorter = Math.Min(w, w / ratio);
        if (shorter < minSide) w *= minSide / shorter;
        double maxW = Math.Min(limit.Width, limit.Height * ratio);
        if (w > maxW) w = Math.Max(maxW, Math.Min(rect.Width, w));
        int nw = (int)Math.Round(w), nh = (int)Math.Round(w / ratio);
        int cx = rect.X + rect.Width / 2, cy = rect.Y + rect.Height / 2;
        return new PixelRect(cx - nw / 2, cy - nh / 2, nw, nh);
    }

    /// <summary>
    /// WM_SIZING with the ratio kept (Orbit's <c>contentAspectRatio</c>): the dragged edge decides,
    /// the opposite edge or corner stays put. <paramref name="ratio"/> is width over height.
    /// </summary>
    public static PixelRect KeepRatio(SizingEdge edge, PixelRect proposed, double ratio, int minSide)
    {
        if (ratio <= 0) return proposed;
        double w = Math.Max(proposed.Width, 1), h = Math.Max(proposed.Height, 1);
        // Top and bottom drags follow the height; left, right and corners follow the width, unless a
        // corner moved mostly vertically.
        bool byHeight = edge is SizingEdge.Top or SizingEdge.Bottom
            || (edge is SizingEdge.TopLeft or SizingEdge.TopRight or SizingEdge.BottomLeft or SizingEdge.BottomRight && h * ratio > w);
        if (byHeight) w = h * ratio;
        else h = w / ratio;
        double shorter = Math.Min(w, h);
        if (shorter < minSide)
        {
            w *= minSide / shorter;
            h *= minSide / shorter;
        }
        int nw = (int)Math.Round(w), nh = (int)Math.Round(h);
        bool anchorRight = edge is SizingEdge.Left or SizingEdge.TopLeft or SizingEdge.BottomLeft;
        bool anchorBottom = edge is SizingEdge.Top or SizingEdge.TopLeft or SizingEdge.TopRight;
        int x = anchorRight ? proposed.Right - nw : proposed.X;
        int y = anchorBottom ? proposed.Bottom - nh : proposed.Y;
        // A side drag keeps the other axis centered on where it was.
        if (edge is SizingEdge.Left or SizingEdge.Right) y = proposed.Y + (proposed.Height - nh) / 2;
        if (edge is SizingEdge.Top or SizingEdge.Bottom) x = proposed.X + (proposed.Width - nw) / 2;
        return new PixelRect(x, y, nw, nh);
    }
}
