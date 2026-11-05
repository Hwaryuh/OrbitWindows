using static Orbit.App.SelfTestParts;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orbit.Core;
using Orbit.Core.Capture;
using Orbit.Core.Capture.Recording;
using Orbit.Core.Settings;

namespace Orbit.App.Features.Capture;

/// <summary>
/// <c>--selftest-video [--render file.png]</c>: the real editor window on a synthetic silent video in a
/// temporary folder, outside every monitor. Nothing plays, so nothing sounds. Exports go through
/// the same paths as the buttons, with a temporary settings file.
/// </summary>
internal static class VideoEditorSelfTest
{
    public static bool Run(string? render)
    {
        var folder = NewFolder();
        var results = new SelfTestLog("Video editor");
        var preferences = new Preferences(Path.Combine(folder, "settings.json"), Pref.Defaults());
        try
        {
            preferences.Set(Pref.CaptureSaveFolder, Path.Combine(folder, "saved"));
            var source = Path.Combine(folder, "Orbit Recording test.mp4");
            WaitFor(VideoExport.WriteTestVideoAsync(source, 640, 360, TimeSpan.FromSeconds(4)));
            var actions = new CaptureActions(new CaptureStore(preferences, Path.Combine(folder, "library")));
            var window = VideoEditorWindow.ForSelfTest(source, actions);
            window.Left = -32000;
            window.Top = -32000;
            window.Width = 1000;
            window.Height = 700;
            window.ShowActivated = false;
            window.Show();
            WaitFor(window.LoadAsync());
            results.Check("the length is read", window.Trim?.Length.TotalSeconds is > 3.9 and < 4.1);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!window.HasFilmstrip && clock.Elapsed < TimeSpan.FromSeconds(20)) Pump();
            results.Check("the filmstrip fills in", window.HasFilmstrip);
            results.Check("nothing plays by itself", !window.IsPlaying);

            WaitFor(window.SaveForTest(gif: false));
            var saved = Path.Combine(folder, "saved", "Orbit Recording test.mp4");
            results.Check("with no edits, saving copies the original", File.Exists(saved) && new FileInfo(saved).Length == new FileInfo(source).Length);

            window.SetEndForTest(start: true, TimeSpan.FromSeconds(1));
            window.SetEndForTest(start: false, TimeSpan.FromSeconds(2.5));
            results.Check("the ends move", window.Trim!.IsEdited(false));
            WaitFor(window.SaveForTest(gif: false));
            var edited = Path.Combine(folder, "saved", "Orbit Recording test (Edited).mp4");
            results.Check("an edit saves under its own name", File.Exists(edited));
            if (File.Exists(edited))
            {
                var info = VideoProbe.Read(edited);
                results.Check("at the kept length", info.Length.TotalSeconds is > 1.4 and < 1.6);
            }
            WaitFor(window.SaveForTest(gif: true));
            var gif = Path.Combine(folder, "saved", "Orbit Recording test (Edited).gif");
            results.Check("a GIF saves", File.Exists(gif) && new FileInfo(gif).Length > 0);

            Pump();
            if (render is not null) Render(window, render);

            // Closing mid-export leaves no file behind.
            window.SetEndForTest(start: false, TimeSpan.FromSeconds(3));
            var running = window.SaveForTest(gif: true);
            window.Close();
            WaitFor(running);
            var drafts = Directory.Exists(VideoEditorWindow.ExportsFolder) ? Directory.GetFiles(VideoEditorWindow.ExportsFolder, "*.part", SearchOption.AllDirectories) : [];
            results.Check("closing during an export leaves no draft", drafts.Length == 0);
            results.Check("the window lets go of the file", CanMove(source));
        }
        catch (Exception e)
        {
            results.Check($"no exception ({e.GetType().Name} at {e.TargetSite?.Name})", false);
        }
        finally
        {
            preferences.Dispose();
            TryDeleteFolder(folder, "Video editor");
        }
        return results.Finish();
    }

    private static bool CanMove(string path)
    {
        var moved = path + ".moved";
        for (int i = 0; i < 40; i++)
        {
            try
            {
                File.Move(path, moved);
                return true;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
                Pump();
            }
        }
        return false;
    }

    /// <summary>Waits with the UI running, so continuations on it go ahead.</summary>
    private static void WaitFor(Task task)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException();
            Pump();
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }
}
