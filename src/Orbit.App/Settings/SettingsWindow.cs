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
public sealed class SettingsWindow : Window
{
    private static SettingsWindow? _open;

    private static readonly Brush Ink = Brushes.Black;
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(110, 110, 110));
    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(230, 230, 230));

    private readonly Preferences _preferences;
    private readonly ContentControl _page = new() { Margin = new Thickness(28, 24, 28, 24) };

    private SettingsWindow(Preferences preferences)
    {
        _preferences = preferences;
        Title = Strings.SettingsTitle;
        Width = 760;
        Height = 520;
        MinWidth = 600;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.White;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Malgun Gothic");

        var pages = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(247, 247, 247)),
            Padding = new Thickness(8, 12, 8, 12),
            Width = 190,
        };
        foreach (var (title, build) in Pages())
            pages.Items.Add(new ListBoxItem { Content = title, Tag = build, Padding = new Thickness(10, 7, 10, 7), FontSize = 14 });
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

    public static void ShowOrActivate(Preferences preferences)
    {
        _open ??= new SettingsWindow(preferences);
        _open.Show();
        if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
        _open.Activate();
    }

    private IEnumerable<(string Title, Func<UIElement> Build)> Pages()
    {
        yield return (Strings.PageGeneral, General);
        foreach (var title in new[]
                 {
                     Strings.PageRing, Strings.PageLauncher, Strings.PageCapture, Strings.PageClipboard, Strings.PageTranslate,
                     Strings.PageSnippets, Strings.PageKeepAwake, Strings.PageSound, Strings.PageSystem, Strings.PageDisplays,
                 })
            yield return (title, () => NotReady(title));
        yield return (Strings.PageAbout, About);
    }

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
