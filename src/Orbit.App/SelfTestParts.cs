using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orbit.Core;

namespace Orbit.App;

/// <summary>A self-test's results, line by line in the log, and whether all passed.</summary>
internal sealed class SelfTestLog(string name)
{
    private bool _passed = true;

    public void Check(string what, bool ok)
    {
        _passed &= ok;
        Log.Info($"{name} self-test {(ok ? "PASS" : "FAIL")}: {what}");
    }

    public bool Finish(string? detail = null)
    {
        Log.Info($"{name} self-test {(_passed ? "PASS" : "FAIL")}{(detail is null ? "" : ": " + detail)}");
        return _passed;
    }
}

/// <summary>What the off-screen self-tests share.</summary>
internal static class SelfTestParts
{
    /// <summary>A new empty folder for one run; nothing of the user's is touched.</summary>
    public static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "orbit-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static void TryDeleteFolder(string folder, string name)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"{name} self-test left its temporary folder behind");
        }
    }

    /// <summary>Lets the UI thread run until it has nothing left to do.</summary>
    public static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Lets the UI thread run this long, for animations to end.</summary>
    public static void PumpFor(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Every element of a type under <paramref name="root"/>, through documents too.</summary>
    public static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T found) yield return found;
            foreach (var deeper in All<T>(child)) yield return deeper;
        }
    }

    /// <summary>The window as a PNG, wherever it is placed.</summary>
    public static void Render(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
