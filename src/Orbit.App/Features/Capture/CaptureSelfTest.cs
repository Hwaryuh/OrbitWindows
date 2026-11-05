using System.Diagnostics;
using Orbit.Core;
using Orbit.Core.Capture;
using Orbit.Core.Display;

namespace Orbit.App.Features.Capture;

/// <summary>
/// <c>--selftest-capture</c>: freezes every monitor as a capture does (<see cref="ScreenFreeze"/>),
/// takes a GDI reference of each, logs the comparison and quits, without an overlay.
/// </summary>
internal static class CaptureSelfTest
{
    /// <summary>Passes when each frame came from WGC, covers its monitor and matches GDI 99.5 %.</summary>
    public static async Task<bool> RunAsync(WgcCapture? capture)
    {
        var monitors = Monitors.All();
        var clock = Stopwatch.StartNew();
        var frames = await ScreenFreeze.AllAsync(capture, monitors);
        long frozenMs = clock.ElapsedMilliseconds;
        var results = new SelfTestLog("Capture");
        for (int i = 0; i < monitors.Count; i++)
        {
            var reference = GdiCapture.Capture(monitors[i].Bounds);
            double match = FrameCompare.MatchRatio(frames[i], reference);
            bool ok = frames[i].Source == "WGC" && frames[i].Bounds == monitors[i].Bounds && match >= 0.995;
            results.Check($"{monitors[i]} via {frames[i].Source} {frames[i].Bounds}, match with GDI {match:P3}", ok);
        }
        return results.Finish($"{monitors.Count} monitors in {frozenMs} ms");
    }
}
