using System.Windows;
using System.Windows.Controls;
using Orbit.App.Design;
using Orbit.App.Features;
using Orbit.Core.Actions;
using Orbit.Core.Launcher;

namespace Orbit.App.Settings;

/// <summary>
/// The launcher: its shortcut, what it finds and how to ask, and the order it learned. The original has no such page
/// (the launcher shows only among its shortcuts); this one says what typing there does, and opens it.
/// </summary>
internal sealed class LauncherPage : StackPanel
{
    private readonly Palette _palette = Palette.Light;
    private readonly PageParts _parts = new(Palette.Light);

    public LauncherPage(AppServices services)
    {
        Children.Add(_parts.PageTitles(Strings.PageLauncher, Strings.LauncherPageSubtitle, bottom: 20));

        var open = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (services.Shortcuts.For(OrbitAction.Launcher) is { } hotkey)
        {
            var keys = KeyCap.Row(hotkey.Labels(), _palette);
            keys.Margin = new Thickness(0, 0, 10, 0);
            open.Children.Add(keys);
        }
        open.Children.Add(_parts.PillButton(Strings.LauncherOpen, prominent: true, () => services.Actions.Perform(OrbitAction.Launcher)));
        var change = _parts.PillButton(Strings.LauncherChangeShortcut, prominent: false, () => SettingsWindow.ShowOrActivate(services, "shortcuts"));
        Children.Add(_parts.Group(null, null,
            _parts.Row(Strings.LauncherOpenTitle, Strings.LauncherOpenDetail, open),
            _parts.Row(Strings.LauncherShortcutTitle, null, change)));

        Children.Add(_parts.Group(Strings.LauncherFinds, Strings.LauncherFindsFooter,
            Example(Strings.LauncherFindApps, "chrome, 메모장"),
            Example(Strings.LauncherFindFiles, Strings.LauncherFindFilesExample),
            Example(Strings.LauncherFindCommands, Strings.LauncherFindCommandsExample),
            Example(Strings.LauncherFindMath, "12*8"),
            Example(Strings.LauncherFindCurrency, "100달러, 5만원"),
            Example(Strings.LauncherFindUnits, "10 km to mi"),
            Example(Strings.LauncherFindWeather, "서울 날씨"),
            Example(Strings.LauncherFindClock, "도쿄 시간")));

        var frecency = new Frecency(services.Preferences);
        Button? clear = null;
        clear = _parts.PillButton(Strings.LauncherClearHistory, prominent: false, () =>
        {
            frecency.Clear();
            clear!.IsEnabled = false;
        });
        clear.IsEnabled = frecency.HasHistory;
        Children.Add(_parts.Group(Strings.LauncherHistory, null, _parts.Row(Strings.LauncherHistoryTitle, Strings.LauncherHistoryDetail, clear)));
    }

    /// <summary>What it finds, and something to type for it, in the typeface the launcher shows typing in.</summary>
    private FrameworkElement Example(string title, string typed) =>
        _parts.Row(title, null, new TextBlock
        {
            Text = typed, FontSize = 12, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center,
            FontFamily = Glyphs.Mono,
        });
}
