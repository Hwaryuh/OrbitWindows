using Orbit.Core.Display;

namespace Orbit.Core.Capture;

/// <summary>
/// Every monitor at once, before anything is shown over them (Orbit freezes every display before
/// its overlay appears): Windows.Graphics.Capture where it works, GDI where it doesn't.
/// </summary>
public static class ScreenFreeze
{
    public static readonly TimeSpan FrameTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>One frame per monitor, in the order given.</summary>
    public static Task<ScreenFrame[]> AllAsync(WgcCapture? wgc, IReadOnlyList<MonitorInfo> monitors) =>
        Task.WhenAll(monitors.Select(m => MonitorAsync(wgc, m)));

    public static async Task<ScreenFrame> MonitorAsync(WgcCapture? wgc, MonitorInfo monitor)
    {
        if (wgc is not null)
        {
            try
            {
                return await wgc.CaptureMonitorAsync(monitor, FrameTimeout);
            }
            catch (Exception e)
            {
                Log.Warn($"WGC failed on {monitor.DeviceName} ({e.GetType().Name}); using GDI");
            }
        }
        return await Task.Run(() => GdiCapture.Capture(monitor.Bounds));
    }

    /// <summary>Same monitors, bounds and scale; the work area may change (an auto-hiding taskbar).</summary>
    public static bool SameLayout(IReadOnlyList<MonitorInfo> before, IReadOnlyList<MonitorInfo> after) =>
        before.Count == after.Count && before.Zip(after).All(p =>
            p.First.Handle == p.Second.Handle && p.First.Bounds == p.Second.Bounds && p.First.Dpi == p.Second.Dpi);
}
