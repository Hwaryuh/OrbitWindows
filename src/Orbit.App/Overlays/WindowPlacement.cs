using System.Windows;
using System.Windows.Interop;
using Orbit.Core.Display;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Orbit.App.Overlays;

/// <summary>Showing an ordinary window where Orbit wants it, in physical pixels.</summary>
internal static class WindowPlacement
{
    /// <summary>
    /// Shows <paramref name="window"/> centered on <paramref name="monitor"/>'s work area at
    /// <paramref name="rect"/>. It is placed before and after showing: arriving on a monitor of
    /// another scale makes WPF resize it, and the second placement undoes that.
    /// </summary>
    public static void ShowAt(Window window, PixelRect rect)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        Place(handle, rect);
        window.Show();
        Place(handle, rect);
    }

    /// <summary>A window of <paramref name="width"/> × <paramref name="height"/> DIPs centered on the monitor's work area, no larger than it.</summary>
    public static PixelRect Centered(MonitorInfo monitor, double width, double height)
    {
        var work = monitor.WorkArea;
        int w = Math.Min(work.Width, (int)Math.Round(width * monitor.Scale)), h = Math.Min(work.Height, (int)Math.Round(height * monitor.Scale));
        return new PixelRect(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h);
    }

    /// <summary>The monitor under the pointer.</summary>
    public static MonitorInfo? PointerMonitor()
    {
        var (x, y) = Monitors.Cursor();
        return Monitors.FromPoint(x, y);
    }

    private static void Place(nint handle, PixelRect rect) =>
        PInvoke.SetWindowPos(new(handle), default, rect.X, rect.Y, rect.Width, rect.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
}
