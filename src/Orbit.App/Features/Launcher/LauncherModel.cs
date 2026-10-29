using System.Diagnostics;
using System.IO;
using Orbit.App.Design;
using Orbit.App.Overlays;
using Orbit.App.Settings;
using Orbit.Core;
using Orbit.Core.Actions;
using Orbit.Core.Launcher;
using Orbit.Core.Settings;

namespace Orbit.App.Features.Launcher;

internal enum IconKind { Glyph, App, File, Mark }

/// <param name="BeforeHide">Run while the launcher still has the foreground (switching windows needs it).</param>
internal sealed record LauncherAction(string Title, string Glyph, Action Perform, string? Keys = null, bool BeforeHide = false);

/// <param name="Icon">A glyph, an app's parsing name, or a file path, by <paramref name="IconKind"/>.</param>
internal sealed record LauncherItem(string Id, string Title, string? Subtitle, IconKind IconKind, string Icon,
    string? Accessory, string Section, IReadOnlyList<LauncherAction> Actions);

/// <summary>
/// What the launcher lists for a query (Orbit: LauncherModel.swift): an answer first, then Orbit's
/// pages, commands and apps ordered by their best match, open windows, Windows Settings pages, and
/// a web search only when nothing here fits. Empty: suggestions from what was used before.
/// </summary>
internal sealed class LauncherModel(AppServices services, AppIndex apps, Frecency frecency)
{
    private static readonly OrbitAction[] Favorites =
    [
        OrbitAction.CaptureArea, OrbitAction.CaptureText, OrbitAction.Clipboard, OrbitAction.CaptureScreen,
        OrbitAction.Translate, OrbitAction.KeepAwake, OrbitAction.RecentCaptures,
    ];

    public IReadOnlyList<LauncherItem> Build(string raw, IReadOnlyList<OpenWindow> windows)
    {
        var q = raw.Trim().Normalize();
        var result = new List<LauncherItem>();

        if (Calculator.Evaluate(q) is { } value)
        {
            var text = Calculator.Format(value);
            result.Add(new LauncherItem("calc", text, $"= {q}", IconKind.Glyph, Glyphs.Calculator, Strings.SectionCalculator,
                Strings.SectionCalculator, [new LauncherAction(Strings.ActionCopyAnswer, Glyphs.Copy, () => Copy(text))]));
        }

        if (q.Length == 0) return [.. result, .. Suggestions()];
        bool answered = result.Count > 0;

        var pageScores = SettingsWindow.PageTitles.Select(p => (p, Fuzzy.Score(q, p.Title))).Where(x => x.Item2 is not null).Select(x => (x.p, x.Item2!.Value)).ToList();
        var commandScores = OrbitActions.All.Select(a => (a, Fuzzy.Score(q, a.Title(), a.Keywords()))).Where(x => x.Item2 is not null).Select(x => (x.a, x.Item2!.Value)).ToList();
        var appScores = apps.Apps.Select(a => (a, Fuzzy.Score(q, a.Name))).Where(x => x.Item2 is not null).Select(x => (x.a, x.Item2!.Value)).ToList();
        // A window matches by its title, or a little below that by its app ("chro" lists every Chrome window).
        var windowScores = windows.Select(w => (w, Best(Fuzzy.Score(q, w.Title), Fuzzy.Score(q, w.AppName) - 100)))
            .Where(x => x.Item2 is not null).Select(x => (x.w, x.Item2!.Value)).ToList();
        var paneScores = SettingsPanes.All.Select(p => (p, Best(Fuzzy.Score(q, p.Korean, p.Keywords), Fuzzy.Score(q, p.English, p.Keywords))))
            .Where(x => x.Item2 is not null).Select(x => (x.p, x.Item2!.Value)).ToList();

        int best = pageScores.Select(x => x.Item2).Concat(commandScores.Select(x => x.Item2)).Concat(appScores.Select(x => x.Item2))
            .Concat(windowScores.Select(x => x.Item2)).Concat(paneScores.Select(x => x.Item2)).DefaultIfEmpty(0).Max();
        int floor = Fuzzy.Floor(best);

        result.AddRange(pageScores.Where(x => x.Item2 >= Math.Max(floor, Fuzzy.Substring - 100)).OrderByDescending(x => x.Item2).Take(2).Select(x => Page(x.p.Id, x.p.Title)));

        var commands = commandScores.Where(x => x.Item2 >= floor).Select(x => (x.a, x.Item2 + frecency.Boost(CommandId(x.a))))
            .OrderByDescending(x => x.Item2).Take(6).Select(x => Command(x.a, Strings.SectionCommands)).ToList();
        var applications = appScores.Where(x => x.Item2 >= floor).Select(x => (x.a, x.Item2 + frecency.Boost(AppId(x.a))))
            .OrderByDescending(x => x.Item2).Take(8).Select(x => App(x.a, Strings.SectionApps)).ToList();
        // Sections in the order of their best match.
        int bestApp = applications.Count > 0 ? Fuzzy.Score(q, applications[0].Title) ?? 0 : 0;
        int bestCommand = commands.Count > 0 ? Fuzzy.Score(q, commands[0].Title) ?? 0 : 0;
        result.AddRange(bestApp >= bestCommand ? [.. applications, .. commands] : [.. commands, .. applications]);

        result.AddRange(windowScores.Where(x => x.Item2 >= floor).OrderByDescending(x => x.Item2).Take(5).Select(x => Window(x.w)));
        result.AddRange(paneScores.Where(x => x.Item2 >= Math.Max(floor, Fuzzy.Keyword)).OrderByDescending(x => x.Item2).Take(3).Select(x => Pane(x.p)));

        // Searching the web is the way out when nothing here fits, not an extra under every search.
        if (q.Length > 1 && best < Fuzzy.Substring - 100 && !answered)
        {
            var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(q);
            result.Add(new LauncherItem("web", Strings.SearchGoogleFor(q), null, IconKind.Glyph, Glyphs.Globe, Strings.SectionWeb, Strings.SectionWeb,
                [new LauncherAction(Strings.ActionSearch, Glyphs.Globe, () => Open(url))]));
        }
        return result;
    }

    public void OpenSettings() => SettingsWindow.ShowOrActivate(services.Preferences);

    /// <summary>Remembers the choice (windows come and go, so not those) and runs it.</summary>
    public void Bump(LauncherItem item)
    {
        if (!item.Id.StartsWith("win.", StringComparison.Ordinal) && item.Id is not ("calc" or "web")) frecency.Bump(item.Id);
    }

    private IEnumerable<LauncherItem> Suggestions()
    {
        var ranked = OrbitActions.All.Where(a => frecency.Boost(CommandId(a)) > 0).OrderByDescending(a => frecency.Boost(CommandId(a)));
        var commands = ranked.Concat(Favorites).Distinct().Take(7).Select(a => Command(a, Strings.SectionSuggestions));
        var recentApps = apps.Apps.Where(a => frecency.Boost(AppId(a)) > 0).OrderByDescending(a => frecency.Boost(AppId(a)))
            .Take(3).Select(a => App(a, Strings.SectionRecentApps));
        return commands.Concat(recentApps);
    }

    private LauncherItem Command(OrbitAction action, string section) =>
        new(CommandId(action), action.Title(), action.GroupTitle(), IconKind.Glyph, Glyphs.Of(action), null, section,
            [new LauncherAction(Strings.ActionRunCommand, Glyphs.Enter, () => services.Actions.Perform(action))]);

    /// <summary>An app: open it; a desktop app also shows its file (Ctrl+Enter), runs elevated, copies its path.</summary>
    private static LauncherItem App(AppEntry app, string section)
    {
        var open = new LauncherAction(Strings.ActionOpen, Glyphs.OpenInNew, () => Run(() => AppIndex.Launch(app)));
        if (app.TargetPath is not { } path)
            return new(AppId(app), app.Name, null, IconKind.App, app.ParsingName, Strings.AccessoryApp, section, [open]);
        return new(AppId(app), app.Name, null, IconKind.App, app.ParsingName, Strings.AccessoryApp, section,
        [
            open,
            new LauncherAction(Strings.ActionOpenFileLocation, Glyphs.Folder, () => Run(() => ShowInExplorer(path)), "Ctrl ↵"),
            new LauncherAction(Strings.ActionRunAsAdmin, Glyphs.Shield, () => RunAsAdministrator(path)),
            new LauncherAction(Strings.ActionCopyPath, Glyphs.Copy, () => Copy(path)),
        ]);
    }

    private static void ShowInExplorer(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });

    /// <summary>Through UAC; saying no there is the user's choice, not a failure.</summary>
    private static void RunAsAdministrator(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(path) ?? "" });
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == ErrorCancelled)
        {
            Log.Info("Run as administrator cancelled at the UAC prompt");
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Log.Error("Run as administrator failed", e);
            Toast.Show(Strings.OpenFailed);
        }
    }

    /// <summary>ERROR_CANCELLED: the UAC prompt was declined.</summary>
    private const int ErrorCancelled = 1223;

    private static LauncherItem Window(OpenWindow window) =>
        new($"win.{window.Handle}", window.Title, window.AppName.Length > 0 ? window.AppName : null,
            window.ExePath is null ? IconKind.Glyph : IconKind.File, window.ExePath ?? Glyphs.Window,
            window.Minimized ? Strings.AccessoryMinimized : Strings.AccessoryWindow, Strings.SectionWindows,
            [
                new LauncherAction(Strings.ActionSwitchToWindow, Glyphs.Window, () => OpenWindows.Focus(window.Handle), BeforeHide: true),
                new LauncherAction(Strings.ActionCloseWindow, Glyphs.Close, () => OpenWindows.Close(window.Handle), BeforeHide: true),
            ]);

    private LauncherItem Page(string id, string title) =>
        new($"page.{id}", title, "Orbit", IconKind.Mark, "", "Orbit", "Orbit",
            [new LauncherAction(Strings.ActionOpen, Glyphs.Settings, () => SettingsWindow.ShowOrActivate(services.Preferences, id))]);

    private static LauncherItem Pane(SettingsPane pane) =>
        new($"pane.{pane.Uri}", Strings.T(pane.Korean, pane.English), Strings.Korean ? pane.English : null,
            IconKind.Glyph, Glyphs.Settings, Strings.SectionWindowsSettings, Strings.SectionWindowsSettings,
            [new LauncherAction(Strings.ActionOpen, Glyphs.Settings, () => Run(() => SettingsPanes.Open(pane)))]);

    /// <summary>The higher of two scores, either of which may be no match.</summary>
    private static int? Best(int? a, int? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    private static string CommandId(OrbitAction action) => $"cmd.{action.Id()}";
    private static string AppId(AppEntry app) => $"app.{app.ParsingName}";

    private static void Open(string url) => Run(() => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));

    /// <summary>Starting something outside Orbit: a failure is told in a toast, not thrown.</summary>
    private static void Run(Action start)
    {
        try
        {
            start();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Log.Error("Launcher could not open an item", e);
            Toast.Show(Strings.OpenFailed);
        }
    }

    private static void Copy(string text)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                Toast.Show(Strings.Copied, text);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // Another app holds the clipboard for a moment.
                Thread.Sleep(30);
            }
        }
        Toast.Show(Strings.CopyFailed);
    }
}
