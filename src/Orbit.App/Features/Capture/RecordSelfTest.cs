using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Orbit.Core;
using Orbit.Core.Capture;
using Orbit.Core.Capture.Recording;
using Orbit.Core.Display;

namespace Orbit.App.Features.Capture;

/// <summary>
/// <c>--selftest-record</c>: records the primary monitor, an odd-sized area and a resized window for
/// 2 s each while a small window flickers (so frames keep coming), reads each file back and logs
/// whether it holds what was recorded.
/// </summary>
internal static class RecordSelfTest
{
    /// <param name="keep">Leaves the files in %TEMP% (their paths are logged) to look at.</param>
    public static async Task<bool> RunAsync(WgcCapture? capture, bool keep)
    {
        var monitor = Monitors.All().First(m => m.IsPrimary);
        var flicker = Flicker(monitor);
        bool borderless = capture?.Borderless ?? false;
        try
        {
            var b = monitor.Bounds;
            var area = new PixelRect(b.X + 20, b.Y + 20, 301, 201);
            var still = new PixelRect(b.Right - 220, b.Bottom - 220, 200, 200);
            var window = new WindowInteropHelper(flicker).Handle;
            (string Name, RecordSource Source, int Fps, int MinFrames, Action<int>? During)[] cases =
            [
                ("screen-60fps", RecordSource.Monitor(monitor), 60, 10, null),
                ("screen-30fps", RecordSource.Monitor(monitor), 30, 10, null),
                // An odd size is cut to even sides.
                ("area", RecordSource.Area(monitor, area), 30, 10, null),
                // A corner far from the flicker, most likely still: its frame is written again each second.
                ("still-area", RecordSource.Area(monitor, still), 30, 2, null),
                // A window that grows, then shrinks below its start size, mid-recording.
                ("window", RecordSource.Window(window, b), 30, 10, step =>
                {
                    if (step == 7) flicker.Width = 260;
                    if (step == 14) flicker.Width = 100;
                }),
            ];
            foreach (var (name, source, fps, minFrames, during) in cases)
            {
                var path = Path.Combine(Path.GetTempPath(), $"orbit-selftest-{name}.mp4");
                var recorder = await ScreenRecorder.StartAsync(source, new RecordOptions(fps, Cursor: true, borderless, AppSound: true), path);
                for (int step = 0; step < 20; step++)
                {
                    during?.Invoke(step);
                    await Task.Delay(100);
                }
                var result = await recorder.StopAsync();
                if (result is null) throw new InvalidOperationException("No result");
                var info = VideoProbe.Read(path);
                bool ok = info.Width == recorder.Width && info.Height == recorder.Height && info.Width % 2 == 0 && info.Height % 2 == 0
                    && info.Length.TotalSeconds is >= 1.5 and <= 3 && info.Frames >= minFrames && result.LastFrame is not null
                    // The sound track runs the whole length.
                    && info.Sound is { } sound && Math.Abs((sound - info.Length).TotalSeconds) < 0.25;
                Log.Info($"Record self-test {(ok ? "PASS" : "FAIL")} {name}: {info.Width}x{info.Height}, {info.Frames} frames, " +
                    $"{info.Length.TotalSeconds:0.00} s, sound {info.Sound?.TotalSeconds ?? 0:0.00} s, {result.Dropped} dropped, {(result.Hardware ? "hardware" : "software")} encoder, " +
                    $"{new FileInfo(path).Length / 1024} KiB");
                if (keep) Log.Info($"Record self-test file: {path}");
                else File.Delete(path);
                if (!ok) return false;
            }
            return true;
        }
        catch (Exception e)
        {
            Log.Error("Record self-test FAIL", e);
            return false;
        }
        finally
        {
            flicker.Close();
        }
    }

    private static Window Flicker(MonitorInfo monitor)
    {
        double scale = monitor.Dpi / 96.0;
        var window = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
            Left = (monitor.Bounds.X + 40) / scale, Top = (monitor.Bounds.Y + 40) / scale, Width = 160, Height = 160,
        };
        var brush = new SolidColorBrush(Colors.Black);
        window.Background = brush;
        int tick = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => brush.Color = (tick++ % 2 == 0) ? Colors.OrangeRed : Colors.SteelBlue;
        window.Closed += (_, _) => timer.Stop();
        window.Show();
        timer.Start();
        return window;
    }
}
