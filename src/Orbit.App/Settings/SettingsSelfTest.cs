using static Orbit.App.SelfTestParts;
using System.IO;
using System.Windows;
using Orbit.Core.Actions;
using Orbit.Core.Input;
using Orbit.Core.Settings;

namespace Orbit.App.Settings;

/// <summary>
/// <c>--selftest-settings [--render file.png]</c>: the settings window's sidebar with a temporary
/// settings file, in a window outside every monitor. Nothing is typed or clicked, and only pages
/// that need no running feature are opened.
/// </summary>
internal static class SettingsSelfTest
{
    public static bool Run(string? render)
    {
        var folder = NewFolder();
        var results = new SelfTestLog("Settings");
        var preferences = new Preferences(Path.Combine(folder, "settings.json"), Pref.Defaults());
        var hooks = new HookThread();
        var foreground = new ForegroundTracker();
        SettingsWindow? window = null;
        try
        {
            var services = new Features.AppServices(preferences, new ActionRunner(), hooks, foreground, (_, _) => { });
            window = new SettingsWindow(services)
            {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowInTaskbar = false, ShowActivated = false,
            };
            window.Show();
            Pump();
            var sidebar = window.Sidebar;

            var shown = sidebar.PageIds.ToList();
            var all = SettingsWindow.PageTitles.Select(p => p.Id).ToList();
            results.Check("every page is in the sidebar, once", shown.Count == all.Count && shown.Order().SequenceEqual(all.Order()));
            results.Check("it opens on General", sidebar.SelectedId == "general");

            (double card, double row) = sidebar.CardAndRow();
            results.Check("the card starts on the selected row", Math.Abs(card - row) < 1);

            sidebar.Select("about");
            Pump();
            results.Check("selecting a page opens it", sidebar.SelectedId == "about");
            sidebar.Select("general");
            Pump();

            sidebar.Select("nothing-like-this");
            results.Check("an unknown page changes nothing", sidebar.SelectedId == "general");

            // Long enough for the slide to end.
            PumpFor(500);
            (card, row) = sidebar.CardAndRow();
            results.Check("the card ends on the selected row", Math.Abs(card - row) < 1);

            if (render is not null)
            {
                Render(window, render);
                // Pages whose controls show the design, and which need no running feature.
                foreach (string page in new[] { "ports", "about", "overlaps" })
                {
                    sidebar.Select(page);
                    PumpFor(500);
                    Render(window, Path.ChangeExtension(render, $".{page}.png"));
                }
            }
            return results.Finish();
        }
        finally
        {
            window?.Close();
            hooks.Dispose();
            foreground.Dispose();
            TryDeleteFolder(folder, "Settings");
        }
    }
}
