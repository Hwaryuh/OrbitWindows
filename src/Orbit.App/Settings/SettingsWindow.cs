using Orbit.App.Design;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Orbit.Core.Settings;

namespace Orbit.App.Settings;

/// <summary>
/// The settings window's frame (Orbit: App/MainWindow.swift): a sidebar of pages and the page.
/// Each feature fills its page as it is built; until then a page says it isn't ready. Black and
/// white, as Orbit. One window: asking again brings it to the front.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private static SettingsWindow? _open;

    private readonly PageParts _parts = new(Palette.Light);

    private readonly Features.AppServices _services;
    private readonly Preferences _preferences;
    private readonly ContentControl _page = new() { Margin = new Thickness(28, 24, 28, 24) };

    /// <summary>The pages, by id and title: the launcher finds them by name.</summary>
    public static IReadOnlyList<(string Id, string Title)> PageTitles { get; } =
    [
        ("general", Strings.PageGeneral), ("shortcuts", Strings.PageShortcuts), ("overlaps", Strings.PageOverlaps), ("ring", Strings.PageRing), ("launcher", Strings.PageLauncher),
        ("capture", Strings.PageCapture), ("clipboard", Strings.PageClipboard), ("translate", Strings.PageTranslate),
        ("snippets", Strings.PageSnippets), ("input", Strings.PageInput), ("keepAwake", Strings.PageKeepAwake), ("sound", Strings.PageSound),
        ("system", Strings.PageSystem), ("ports", Strings.PagePorts), ("displays", Strings.PageDisplays), ("about", Strings.PageAbout),
    ];

    /// <summary>Internal for the self-test, which places it off screen; anything else opens it through <see cref="ShowOrActivate"/>.</summary>
    internal SettingsWindow(Features.AppServices services)
    {
        _services = services;
        _preferences = services.Preferences;
        Title = Strings.SettingsTitle;
        // Wide enough for the ring page's ring beside its action list.
        Width = 960;
        // Every page in the sidebar without scrolling, where the screen allows.
        Height = Math.Min(830, SystemParameters.WorkArea.Height - 40);
        MinWidth = 860;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.Light.Canvas;
        // Always light, whatever Windows uses, and so are its controls.
        Palette.Light.ApplyTo(Resources);
        FontFamily = (FontFamily)FindResource("OrbitTextFont");

        var pages = Sidebar = new SettingsSidebar(PageTitles);
        var builders = PageTitles.ToDictionary(p => p.Id, p => Build(p.Id, p.Title));
        pages.PageSelected += id => _page.Content = builders[id]();

        var layout = new DockPanel();
        DockPanel.SetDock(pages, Dock.Left);
        layout.Children.Add(pages);
        layout.Children.Add(new Border { BorderBrush = Palette.Light.Hairline, BorderThickness = new Thickness(1, 0, 0, 0), Child = new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        Content = layout;
        pages.Select(PageTitles[0].Id);
        if (services.KeepAwake is { } awake)
        {
            void ShowAwake() => pages.SetDot("keepAwake", awake.Session is not null);
            awake.Changed += ShowAwake;
            Closed += (_, _) => awake.Changed -= ShowAwake;
            ShowAwake();
        }
        Closed += (_, _) => _open = null;
    }

    internal SettingsSidebar Sidebar { get; }

    /// <param name="page">A page id from <see cref="PageTitles"/>; null keeps the one shown.</param>
    public static void ShowOrActivate(Features.AppServices services, string? page = null)
    {
        _open ??= new SettingsWindow(services);
        if (page is not null) _open.Sidebar.Select(page);
        _open.Show();
        if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
        _open.Activate();
    }

    private Func<UIElement> Build(string id, string title) => id switch
    {
        "general" => General,
        "shortcuts" => () => new ShortcutsPage(_services),
        "overlaps" => () => new OverlapsPage(_services),
        "ring" => () => new RingPage(_services),
        "capture" => () => new CapturePage(_services),
        "clipboard" => () => new ClipboardPage(_services),
        "translate" => () => new TranslatePage(_services),
        "snippets" => () => new SnippetsPage(_services),
        "input" => () => new InputPage(_services),
        "keepAwake" => () => new KeepAwakePage(_services),
        "sound" => () => new SoundPage(),
        "system" => () => new SystemPage(_services),
        "ports" => () => new PortsPage(),
        "displays" => () => new DisplaysPage(_services),
        "about" => About,
        _ => () => NotReady(title),
    };

    private UIElement General()
    {
        var panel = new StackPanel();
        panel.Children.Add(_parts.PageTitles(Strings.PageGeneral, Strings.GeneralSubtitle, bottom: 20));
        CheckBox? startup = null;
        startup = _parts.Toggle(_preferences.Get<bool>(Pref.LaunchAtSignIn), on =>
        {
            if (StartupRegistration.Apply(on)) _preferences.Set(Pref.LaunchAtSignIn, on);
            else
            {
                startup!.IsChecked = !on;
                Overlays.Toast.Show(Strings.StartupChangeFailed);
            }
        });
        panel.Children.Add(_parts.Group(null, null, _parts.Row(Strings.LaunchAtSignIn, Strings.LaunchAtSignInDetail, startup)));
        return panel;
    }

    private UIElement NotReady(string title)
    {
        var panel = new StackPanel();
        panel.Children.Add(_parts.PageTitles(title, Strings.PageNotReady));
        return panel;
    }

    private UIElement About()
    {
        var panel = new StackPanel();
        panel.Children.Add(_parts.PageTitles(Strings.PageAbout, Strings.AboutSubtitle, bottom: 20));
        var version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var logs = _parts.PillButton(Strings.AboutLogs, prominent: false, Shell.OpenLogFolder);
        var notices = _parts.PillButton(Strings.AboutNotices, prominent: false, () => Shell.Open(System.IO.Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt")));
        panel.Children.Add(_parts.Group(null, Strings.AboutNotice,
            _parts.Row(Strings.AboutVersion(version), null, logs),
            _parts.Row(Strings.AboutNotices, null, notices)));
        return panel;
    }
}
