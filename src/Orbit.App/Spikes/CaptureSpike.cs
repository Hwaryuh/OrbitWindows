using System.Diagnostics;
using System.IO;
using Orbit.App.Features;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Capture;
using Orbit.Core.Display;

namespace Orbit.App.Spikes;

/// <summary>
/// M0 capture spike (docs/plan/03-milestones.md): freeze every monitor with WGC, then a BitBlt
/// reference; only when all frames are in, show one overlay per monitor. A selection is cropped
/// from the WGC frame, compared with the reference and saved, both, for a look by eye.
/// </summary>
internal sealed class CaptureSpike : IFeature
{
    private readonly List<CaptureOverlayWindow> _overlays = [];
    private bool _running;
    private AppServices? _services;

    public void Start(AppServices services) => _services = services;

    private void Notify(string title, string message) => _services?.Notify(title, message);

    public async Task RunAsync()
    {
        if (_running) return;
        _running = true;
        try
        {
            // The tray menu must be gone from the screen first.
            await Task.Delay(250);
            var monitors = Monitors.All();
            var (frames, references, frozenMs) = await FreezeAsync(monitors);

            for (int i = 0; i < monitors.Count; i++)
            {
                double match = FrameCompare.MatchRatio(frames[i], references[i]);
                Log.Info($"Capture spike: {monitors[i]} via {frames[i].Source}, {frames[i].Width}x{frames[i].Height}, full-frame match with GDI {match:P3}");
            }
            Log.Info($"Capture spike: {monitors.Count} monitors frozen in {frozenMs} ms");

            if (!ScreenFreeze.SameLayout(monitors, Monitors.All()))
            {
                Log.Warn("Capture spike cancelled: the monitor layout changed while capturing");
                Notify(Strings.CaptureResultTitle, Strings.CaptureCancelled);
                return;
            }
            ShowOverlays(monitors, frames, references);
        }
        catch (Exception e)
        {
            Log.Error("Capture spike failed", e);
            Notify(Strings.CaptureResultTitle, Strings.CaptureFailed);
            CloseOverlays();
        }
        finally
        {
            if (_overlays.Count == 0) _running = false;
        }
    }

    /// <summary>Every monitor through WGC, compared with GDI. Passes when each frame came from WGC and matches 99.5 %.</summary>
    public async Task<bool> SelfTestAsync()
    {
        var monitors = Monitors.All();
        var (frames, references, frozenMs) = await FreezeAsync(monitors);
        bool passed = true;
        for (int i = 0; i < monitors.Count; i++)
        {
            double match = FrameCompare.MatchRatio(frames[i], references[i]);
            bool ok = frames[i].Source == "WGC" && frames[i].Bounds == monitors[i].Bounds && match >= 0.995;
            passed &= ok;
            Log.Info($"Capture self-test {(ok ? "PASS" : "FAIL")}: {monitors[i]} via {frames[i].Source} {frames[i].Bounds}, match with GDI {match:P3}");
        }
        Log.Info($"Capture self-test {(passed ? "PASS" : "FAIL")}: {monitors.Count} monitors in {frozenMs} ms");
        return passed;
    }

    /// <summary>All monitors at once through WGC (GDI where it fails), then a GDI reference of each.</summary>
    private async Task<(ScreenFrame[] Frames, ScreenFrame[] References, long FrozenMs)> FreezeAsync(IReadOnlyList<MonitorInfo> monitors)
    {
        var clock = Stopwatch.StartNew();
        var frames = await ScreenFreeze.AllAsync(_services?.Capture, monitors);
        long frozenMs = clock.ElapsedMilliseconds;
        var references = monitors.Select(m => GdiCapture.Capture(m.Bounds)).ToArray();
        return (frames, references, frozenMs);
    }

    private void ShowOverlays(IReadOnlyList<MonitorInfo> monitors, ScreenFrame[] frames, ScreenFrame[] references)
    {
        for (int i = 0; i < monitors.Count; i++)
        {
            var frame = frames[i];
            var overlay = new CaptureOverlayWindow(monitors[i], frame);
            overlay.Selected += area => Finish(area, frames, references);
            overlay.Cancelled += CloseOverlays;
            // Closed some other way (Alt+F4): close the rest too.
            overlay.Closed += (_, _) => CloseOverlays();
            _overlays.Add(overlay);
            overlay.ShowAt(monitors[i].Bounds);
            Log.Info($"Capture overlay on {monitors[i].DeviceName} placed at {overlay.Placement}");
        }
        // The overlay under the pointer takes the keys.
        var (x, y) = Monitors.Cursor();
        (_overlays.FirstOrDefault(o => o.Monitor.Bounds.Contains(x, y)) ?? _overlays[0]).Activate();
    }

    private void Finish(PixelRect area, ScreenFrame[] frames, ScreenFrame[] references)
    {
        var index = Array.FindIndex(frames, f => f.Bounds.Contains(area.X, area.Y));
        CloseOverlays();
        if (index < 0) return;

        var captured = frames[index].Crop(area);
        var reference = references[index].Crop(area);
        double match = FrameCompare.MatchRatio(captured, reference);
        var folder = AppPaths.LocalFolder("spike");
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Save(captured, Path.Combine(folder, $"capture-{stamp}-{captured.Source.ToLowerInvariant()}.png"));
        Save(reference, Path.Combine(folder, $"capture-{stamp}-gdi-reference.png"));
        Log.Info($"Capture spike selection {area} via {captured.Source}: {captured.Width}x{captured.Height}, match with GDI {match:P3}, saved to {folder}");
        Notify(Strings.CaptureResultTitle, Strings.CaptureResult(captured.Width, captured.Height, match, captured.Source));
    }

    private void CloseOverlays()
    {
        // Closing one raises Closed, which comes back here: take the list first.
        var overlays = _overlays.ToArray();
        _overlays.Clear();
        foreach (var overlay in overlays) overlay.Close();
        _running = false;
    }

    private static void Save(ScreenFrame frame, string path) =>
        File.WriteAllBytes(path, Features.Capture.FrameImages.Png(Features.Capture.FrameImages.ToBitmap(frame)));
}
