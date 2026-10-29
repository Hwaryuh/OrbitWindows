using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace Orbit.Core.Display;

/// <summary>One monitor, in physical pixels. <see cref="Dpi"/> is the effective DPI (96 at 100 %).</summary>
public sealed record MonitorInfo(nint Handle, string DeviceName, PixelRect Bounds, PixelRect WorkArea, uint Dpi, bool IsPrimary)
{
    public double Scale => Dpi / 96.0;

    /// <summary>Physical pixels to device-independent units on this monitor.</summary>
    public double ToDip(int pixels) => pixels / Scale;

    public override string ToString() => $"{DeviceName} {Bounds} {Dpi}dpi{(IsPrimary ? " primary" : "")}";
}

/// <summary>
/// The monitors as Windows reports them to a per-monitor-v2 aware process. Every coordinate is
/// physical pixels; convert to DIPs only inside a window, with that window's monitor.
/// </summary>
public static class Monitors
{
    public static unsafe IReadOnlyList<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, (monitor, _, _, _) =>
        {
            if (Describe(monitor) is { } info) list.Add(info);
            return true;
        }, default);
        return list;
    }

    public static MonitorInfo? FromPoint(int x, int y) => Describe(Nearest(x, y));

    /// <summary>The effective DPI of the monitor at a point. Allocation-free, for the hook thread.</summary>
    public static uint DpiAt(int x, int y) => DpiOf(Nearest(x, y));

    private static HMONITOR Nearest(int x, int y) =>
        PInvoke.MonitorFromPoint(new System.Drawing.Point(x, y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);

    private static uint DpiOf(HMONITOR monitor) =>
        PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded ? dpiX : 96;

    public static (int X, int Y) Cursor()
    {
        PInvoke.GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    /// <summary>The union of all monitors' bounds.</summary>
    public static PixelRect VirtualScreen(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) return default;
        int left = monitors.Min(m => m.Bounds.X), top = monitors.Min(m => m.Bounds.Y);
        int right = monitors.Max(m => m.Bounds.Right), bottom = monitors.Max(m => m.Bounds.Bottom);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    private static unsafe MonitorInfo? Describe(HMONITOR monitor)
    {
        var info = new MONITORINFOEXW();
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info)) return null;

        uint dpi = DpiOf(monitor);

        var name = info.szDevice.ToString();
        var bounds = ToPixelRect(info.monitorInfo.rcMonitor);
        var work = ToPixelRect(info.monitorInfo.rcWork);
        bool primary = (info.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0;
        return new MonitorInfo(monitor, name, bounds, work, dpi, primary);
    }

    private static PixelRect ToPixelRect(RECT r) => new(r.left, r.top, r.right - r.left, r.bottom - r.top);
}
