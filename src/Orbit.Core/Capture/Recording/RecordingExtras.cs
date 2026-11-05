using Orbit.Core.Display;

namespace Orbit.Core.Capture.Recording;

/// <summary>The numbers behind the countdown and the click rings.</summary>
public static class RecordingExtras
{
    /// <summary>A click ring's window, in points: wide enough for the ring at its largest.</summary>
    public const double RingSide = 70;

    /// <summary>The countdown's window, in points.</summary>
    public const double CountdownSide = 200;

    /// <summary>The seconds to count: the choices offered (off, 3, 5), and Orbit's 3 for anything else in the file.</summary>
    public static int CountdownSeconds(int preference) => preference is 0 or 3 or 5 ? preference : 3;

    /// <summary>A ring's window, centered on the click.</summary>
    public static PixelRect RingPlace(int x, int y, double scale)
    {
        int side = (int)Math.Round(RingSide * scale);
        return new PixelRect(x - side / 2, y - side / 2, side, side);
    }

    /// <summary>
    /// The countdown's window over the middle of what is about to be recorded, kept inside the work
    /// area: a window being recorded may hang off its monitor.
    /// </summary>
    public static PixelRect CountdownPlace(PixelRect target, PixelRect work, double scale)
    {
        int side = (int)Math.Round(CountdownSide * scale);
        int x = target.X + (target.Width - side) / 2, y = target.Y + (target.Height - side) / 2;
        return new PixelRect(Math.Clamp(x, work.X, Math.Max(work.X, work.Right - side)), Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - side)), side, side);
    }
}
