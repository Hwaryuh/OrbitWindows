using System.Diagnostics;
using System.Globalization;
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
/// <param name="Keys">A command's shortcut, shown as keys in place of <paramref name="Accessory"/>.</param>
internal sealed record LauncherItem(string Id, string Title, string? Subtitle, IconKind IconKind, string Icon,
    string? Accessory, string Section, IReadOnlyList<LauncherAction> Actions, IReadOnlyList<string>? Keys = null);

/// <summary>
/// What the launcher lists for a query (Orbit: LauncherModel.swift): answers first (calculator,
/// currency or a conversion, world clock, weather), then Orbit's pages, commands and apps ordered
/// by their best match, open windows, Windows Settings pages, files, and a web search only when
/// nothing here fits. Empty: suggestions from what was used before.
/// </summary>
internal sealed class LauncherModel(AppServices services, AppIndex apps, Frecency frecency,
    ExchangeRates rates, WeatherService weather, FileSearch files)
{
    /// <summary>Rates, a weather report or file results arrived: build again. Raised on a background thread.</summary>
    public event Action? AnswersChanged
    {
        add
        {
            rates.Changed += value;
            weather.Changed += value;
            files.Changed += value;
        }
        remove
        {
            rates.Changed -= value;
            weather.Changed -= value;
            files.Changed -= value;
        }
    }

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
            result.Add(Answer("calc", text, $"= {q}", Glyphs.Calculator, Strings.SectionCalculator, text));
        }

        if (q.Length == 0)
        {
            files.Results("");
            weather.Forget();
            return [.. result, .. Suggestions()];
        }
        bool answered = result.Count > 0;
        // Orbit's order: currency, else a conversion; then a world clock and the weather.
        foreach (var answer in new[] { CurrencyAnswer(q) ?? UnitAnswer(q), ClockAnswer(q), WeatherAnswer(q) })
        {
            if (answer is not { } a) continue;
            result.Add(a.Item);
            answered |= a.Answers;
        }

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
        var found = files.Results(q);
        result.AddRange(found.Take(8).Select(File));

        // Searching the web is the way out when nothing here fits, not an extra under every search.
        if (q.Length > 1 && best < Fuzzy.Substring - 100 && !answered && found.Count == 0)
        {
            var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(q);
            result.Add(new LauncherItem("web", Strings.SearchGoogleFor(q), null, IconKind.Glyph, Glyphs.Globe, Strings.SectionWeb, Strings.SectionWeb,
                [new LauncherAction(Strings.ActionSearch, Glyphs.Globe, () => Open(url))]));
        }
        return result;
    }

    public void OpenSettings() => SettingsWindow.ShowOrActivate(services.Preferences);

    /// <summary>Remembers the choice: not windows (they come and go), answers, or files (paths stay out of the settings).</summary>
    public void Bump(LauncherItem item)
    {
        if (item.Id is "calc" or "web" or "fx" or "unit" or "weather") return;
        if (item.Id.StartsWith("win.", StringComparison.Ordinal) || item.Id.StartsWith("clock.", StringComparison.Ordinal)
            || item.Id.StartsWith("file.", StringComparison.Ordinal))
            return;
        frecency.Bump(item.Id);
    }

    /// <summary>The amount in another currency. While no rates are in hand, a row says they are loading or failed.</summary>
    private (LauncherItem Item, bool Answers)? CurrencyAnswer(string q)
    {
        if (Currencies.Parse(q, Currencies.Local) is not { } query) return null;
        if (rates.Rate(query.From, query.To) is { } rate)
        {
            double converted = query.Value * rate;
            var updated = rates.Updated is { } when ? " · " + Strings.RatesFrom(when) : "";
            var subtitle = $"{Currencies.Format(query.Value, query.From)} · 1 {query.From} = {QueryWords.Format(rate, 6)} {query.To}{updated}";
            return (Answer("fx", Currencies.Format(converted, query.To), subtitle, Glyphs.Currency, Strings.SectionCurrency, QueryWords.Format(converted, 2)), true);
        }
        // Rates are in hand but not for this code: no answer, as Orbit.
        if (rates.HasRates) return null;
        var pair = $"{query.From} → {query.To}";
        return rates.Failed
            ? (Status("fx", Strings.RatesFailed, pair, Glyphs.Currency, Strings.SectionCurrency), false)
            : (Status("fx", Strings.RatesLoading, pair, Glyphs.Currency, Strings.SectionCurrency), true);
    }

    private static (LauncherItem Item, bool Answers)? UnitAnswer(string q)
    {
        if (Units.Answer(q) is not { } a) return null;
        return (Answer("unit", Units.Format(a.Converted, a.To), "= " + Units.Format(a.Value, a.From), Glyphs.Ruler,
            Strings.SectionConversion, QueryWords.Format(a.Converted)), true);
    }

    /// <summary>The time in a city now, its date, and how far it is from here (to the minute: India is +3:30 from Korea).</summary>
    private static (LauncherItem Item, bool Answers)? ClockAnswer(string q)
    {
        if (WorldClock.Parse(q) is not { } city) return null;
        var now = DateTimeOffset.Now;
        var there = TimeZoneInfo.ConvertTime(now, city.Zone);
        var time = there.ToString("t", CultureInfo.CurrentCulture);
        var date = $"{there.ToString("m", CultureInfo.CurrentCulture)} ({there.ToString("ddd", CultureInfo.CurrentCulture)})";
        var offset = WorldClock.OffsetFromLocal(city.Zone, now);
        var span = Strings.Hours((int)Math.Abs(offset.TotalHours), Math.Abs(offset.Minutes));
        var relation = offset == TimeSpan.Zero ? Strings.ClockSame : offset > TimeSpan.Zero ? Strings.ClockAhead(span) : Strings.ClockBehind(span);
        return (Answer($"clock.{city.Zone.Id}", time, $"{city.Name} · {date} · {relation}", Glyphs.Clock, Strings.SectionWorldClock, time), true);
    }

    private (LauncherItem Item, bool Answers)? WeatherAnswer(string q)
    {
        if (WeatherService.Parse(q) is not { } place)
        {
            weather.Forget();
            return null;
        }
        var (status, report) = weather.Get(place);
        if (report is { } r)
        {
            var title = $"{(int)Math.Round(r.Temperature)}° · {Strings.WeatherLabel(r.Kind)}";
            var subtitle = $"{r.City} · {Strings.HighLow((int)Math.Round(r.High), (int)Math.Round(r.Low))}";
            return (new LauncherItem("weather", title, subtitle, IconKind.Glyph, r.Kind == WeatherKind.Clear ? Glyphs.Sun : Glyphs.Cloud,
                Strings.SectionWeather, Strings.SectionWeather, [new LauncherAction(Strings.ActionCopy, Glyphs.Copy, () => Copy($"{r.City} {title}"))]), true);
        }
        return status == WeatherStatus.Failed
            ? (Status("weather", Strings.WeatherFailed, place, Glyphs.Cloud, Strings.SectionWeather), false)
            : (Status("weather", Strings.WeatherLoading, place, Glyphs.Cloud, Strings.SectionWeather), true);
    }

    private static LauncherItem Answer(string id, string title, string subtitle, string glyph, string section, string copy) =>
        new(id, title, subtitle, IconKind.Glyph, glyph, section, section, [new LauncherAction(Strings.ActionCopyAnswer, Glyphs.Copy, () => Copy(copy))]);

    /// <summary>A row that only tells something (loading, failed): Enter does nothing.</summary>
    private static LauncherItem Status(string id, string title, string subtitle, string glyph, string section) =>
        new(id, title, subtitle, IconKind.Glyph, glyph, section, section, []);

    /// <summary>A file or folder: open it, show it in Explorer (Ctrl+Enter), copy its path.</summary>
    private static LauncherItem File(string path) =>
        new($"file.{path}", Path.GetFileName(path), Path.GetDirectoryName(path), IconKind.File, path, Strings.AccessoryFile, Strings.SectionFiles,
        [
            new LauncherAction(Strings.ActionOpen, Glyphs.OpenInNew, () => Open(path)),
            new LauncherAction(Strings.ActionOpenFileLocation, Glyphs.Folder, () => Run(() => ShowInExplorer(path)), "Ctrl ↵"),
            new LauncherAction(Strings.ActionCopyPath, Glyphs.Copy, () => Copy(path)),
        ]);

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
            [new LauncherAction(Strings.ActionRunCommand, Glyphs.Enter, () => services.Actions.Perform(action))],
            services.ShortcutFor(action)?.Labels());

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
        Capture.Clipboards.CopyText(text, Strings.Copied, text);
    }
}
