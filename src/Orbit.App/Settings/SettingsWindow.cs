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

    private static readonly Brush Ink = Brushes.Black;
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(110, 110, 110));
    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(230, 230, 230));

    private readonly Features.AppServices _services;
    private readonly Preferences _preferences;
    private readonly ContentControl _page = new() { Margin = new Thickness(28, 24, 28, 24) };
    private readonly ListBox _pages;

    /// <summary>The pages, by id and title: the launcher finds them by name.</summary>
    public static IReadOnlyList<(string Id, string Title)> PageTitles { get; } =
    [
        ("general", Strings.PageGeneral), ("shortcuts", Strings.PageShortcuts), ("overlaps", Strings.PageOverlaps), ("ring", Strings.PageRing), ("launcher", Strings.PageLauncher),
        ("capture", Strings.PageCapture), ("clipboard", Strings.PageClipboard), ("translate", Strings.PageTranslate),
        ("snippets", Strings.PageSnippets), ("input", Strings.PageInput), ("keepAwake", Strings.PageKeepAwake), ("sound", Strings.PageSound),
        ("system", Strings.PageSystem), ("ports", Strings.PagePorts), ("displays", Strings.PageDisplays), ("about", Strings.PageAbout),
    ];

    private SettingsWindow(Features.AppServices services)
    {
        _services = services;
        _preferences = services.Preferences;
        Title = Strings.SettingsTitle;
        // Wide enough for the ring page's ring beside its action list.
        Width = 960;
        Height = 700;
        MinWidth = 860;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Malgun Gothic");

        var pages = _pages = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(247, 247, 247)),
            Padding = new Thickness(8, 12, 8, 12),
            Width = 190,
        };
        foreach (var (id, title) in PageTitles)
            pages.Items.Add(new ListBoxItem { Content = title, Tag = Build(id, title), Padding = new Thickness(10, 7, 10, 7), FontSize = 14 });
        pages.SelectionChanged += (_, _) =>
        {
            if (pages.SelectedItem is ListBoxItem { Tag: Func<UIElement> build }) _page.Content = build();
        };

        var layout = new DockPanel();
        DockPanel.SetDock(pages, Dock.Left);
        layout.Children.Add(pages);
        layout.Children.Add(new Border { BorderBrush = Line, BorderThickness = new Thickness(1, 0, 0, 0), Child = new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        Content = layout;
        pages.SelectedIndex = 0;
        Closed += (_, _) => _open = null;
    }

    /// <param name="page">A page id from <see cref="PageTitles"/>; null keeps the one shown.</param>
    public static void ShowOrActivate(Features.AppServices services, string? page = null)
    {
        _open ??= new SettingsWindow(services);
        int index = PageTitles.ToList().FindIndex(p => p.Id == page);
        if (index >= 0) _open._pages.SelectedIndex = index;
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
        "input" => () => new InputPage(_services.Araso!),
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
        var panel = Page(Strings.PageGeneral);
        var startup = new CheckBox
        {
            Content = Strings.LaunchAtSignIn,
            IsChecked = _preferences.Get<bool>(Pref.LaunchAtSignIn),
            FontSize = 14,
            Foreground = Ink,
            Margin = new Thickness(0, 4, 0, 2),
        };
        startup.Click += (_, _) =>
        {
            bool on = startup.IsChecked == true;
            if (StartupRegistration.Apply(on)) _preferences.Set(Pref.LaunchAtSignIn, on);
            else
            {
                startup.IsChecked = !on;
                Overlays.Toast.Show(Strings.StartupChangeFailed);
            }
        };
        panel.Children.Add(startup);
        panel.Children.Add(Caption(Strings.LaunchAtSignInDetail));
        return panel;
    }

    private static UIElement NotReady(string title)
    {
        var panel = Page(title);
        panel.Children.Add(Caption(Strings.PageNotReady));
        return panel;
    }

    private static UIElement About()
    {
        var panel = Page(Strings.PageAbout);
        var version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        panel.Children.Add(new TextBlock { Text = Strings.AboutVersion(version), FontSize = 14, Foreground = Ink });
        panel.Children.Add(Caption(Strings.AboutNotice));
        var logs = new Button { Content = Strings.AboutLogs, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 16, 0, 0) };
        logs.Click += (_, _) => Shell.OpenLogFolder();
        panel.Children.Add(logs);
        return panel;
    }

    private static StackPanel Page(string title)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(0, 0, 0, 16) });
        return panel;
    }

    private static TextBlock Caption(string text) =>
        new() { Text = text, FontSize = 12.5, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
}
