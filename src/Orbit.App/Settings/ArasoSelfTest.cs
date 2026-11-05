using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Orbit.App.Features;
using Orbit.Core.Araso;
using static Orbit.App.SelfTestParts;

namespace Orbit.App.Settings;

/// <summary>
/// <c>--selftest-araso [--render file.png]</c>: the page in a window outside every monitor, with
/// Araso's files in a temporary folder. It only reads what Windows says about Araso: nothing is
/// registered or switched.
/// </summary>
internal static class ArasoSelfTest
{
    public static bool Run(string? render)
    {
        var folder = NewFolder();
        var results = new SelfTestLog("Araso");
        try
        {
            var feature = new ArasoFeature(folder);
            var page = new InputPage(feature);
            var window = new Window
            {
                Left = -32000, Top = -32000, Width = 760, Height = 900, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
                Background = Brushes.White, Content = new ScrollViewer { Content = new Border { Padding = new Thickness(24), Child = page } },
            };
            window.Show();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!feature.Known && clock.Elapsed < TimeSpan.FromSeconds(20))
            {
                Thread.Sleep(20);
                Pump();
            }
            Pump();
            results.Check($"the state is read in {clock.ElapsedMilliseconds} ms: {feature.State}", feature.Known);
            var state = feature.State;
            results.Check("the next step fits what is true", state.IsInstalled && state.Registered || state.Next == ArasoStep.NotPresent);
            results.Check("the page offers no download", !All<Button>(page).Any(b => (b.Content as string)?.Contains("Install") == true || (b.Content as string)?.Contains("설치") == true));

            page.AddWord("chrao");
            page.AddWord("two words");
            page.AddReplacement("btw", "그런데");
            var store = new ArasoFileStore(folder, () => false, () => { });
            results.Check("a word is written", ArasoFiles.Words(store.Read(ArasoFileStore.Dictionary)).SequenceEqual([new ArasoWord("chrao", "korean")]));
            results.Check("a replacement is written", ArasoFiles.Replacements(store.Read(ArasoFileStore.Replacements)).Single().Value == "그런데");

            File.WriteAllText(Path.Combine(folder, ArasoFileStore.Dictionary), "{broken");
            page.AddWord("other");
            results.Check("a broken file is left alone", File.ReadAllText(Path.Combine(folder, ArasoFileStore.Dictionary)) == "{broken");
            Pump();
            if (render is not null) Render(window, render);
            window.Close();
        }
        catch (Exception e)
        {
            results.Check($"no exception ({e.GetType().Name} at {e.TargetSite?.Name})", false);
        }
        finally
        {
            TryDeleteFolder(folder, "Araso");
        }
        return results.Finish();
    }
}
