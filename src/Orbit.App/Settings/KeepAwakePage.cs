using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Orbit.App.Design;
using Orbit.App.Features;
using Orbit.App.Overlays;
using Orbit.Core.Launcher;
using Orbit.Core.Power;
using Orbit.Core.Settings;

namespace Orbit.App.Settings;

/// <summary>
/// The Keep Awake page (Orbit: <c>AwakePage</c>): what is running and how long is left, sessions
/// to start, whether the screen stays on, and the battery stop.
/// </summary>
internal sealed class KeepAwakePage : StackPanel
{
    private readonly KeepAwakeFeature _awake;
    private readonly Preferences _preferences;
    private readonly Palette _palette = Palette.Light;
    private readonly PageParts _parts = new(Palette.Light);
    private readonly TextBlock _status = new() { FontSize = 18, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _detail = new() { FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
    private readonly Button _stop;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public KeepAwakePage(AppServices services)
    {
        _awake = services.KeepAwake!;
        _preferences = services.Preferences;
        var header = _parts.PageTitles(Strings.PageKeepAwake, Strings.AwakeSubtitle, bottom: 20);
        Children.Add(header);

        _stop = _parts.PillButton(Strings.AwakeStop, prominent: true, () => _awake.End(Strings.AwakeStopped));
        var hero = new DockPanel();
        _stop.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(_stop, Dock.Right);
        hero.Children.Add(_stop);
        var text = new StackPanel();
        _status.Foreground = _palette.Ink;
        _detail.Foreground = _palette.InkSecondary;
        text.Children.Add(_status);
        text.Children.Add(_detail);
        hero.Children.Add(text);
        var heroCard = _parts.Card(hero, 18);
        heroCard.Margin = new Thickness(0, 0, 0, 28);
        Children.Add(heroCard);

        var durations = new WrapPanel();
        foreach (var (title, span) in new (string, TimeSpan?)[]
        {
            (Strings.AwakeMinutes(15), TimeSpan.FromMinutes(15)), (Strings.AwakeMinutes(30), TimeSpan.FromMinutes(30)), (Strings.AwakeHours(1), TimeSpan.FromHours(1)),
            (Strings.AwakeHours(2), TimeSpan.FromHours(2)), (Strings.AwakeHours(4), TimeSpan.FromHours(4)), (Strings.AwakeHours(8), TimeSpan.FromHours(8)), (Strings.AwakeForever, null),
        })
        {
            var pill = _parts.PillButton(title, prominent: false, () => _awake.Begin(span is { } s ? AwakeSession.For(s, DateTime.UtcNow) : AwakeSession.Indefinite));
            pill.Margin = new Thickness(0, 0, 6, 6);
            durations.Children.Add(pill);
        }
        var start = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        start.Children.Add(_parts.SectionTitle(Strings.AwakeStartSession, bottom: 10));
        start.Children.Add(durations);
        Children.Add(start);

        var time = new TextBox { Width = 70, FontSize = 13, Padding = new Thickness(6, 3, 6, 3), Text = DateTime.Now.AddHours(2).ToString("HH:mm", CultureInfo.InvariantCulture), VerticalContentAlignment = VerticalAlignment.Center };
        var untilRow = new StackPanel { Orientation = Orientation.Horizontal };
        untilRow.Children.Add(time);
        var startUntil = _parts.PillButton(Strings.AwakeStart, prominent: false, () =>
        {
            if (!TimeOnly.TryParseExact(time.Text.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            {
                Toast.Show(Strings.AwakeTimeInvalid);
                return;
            }
            _awake.Begin(AwakeSession.UntilTime(at, DateTime.Now, TimeZoneInfo.Local));
        });
        startUntil.Margin = new Thickness(8, 0, 0, 0);
        untilRow.Children.Add(startUntil);

        var apps = new ComboBox { Width = 200, FontSize = 13 };
        foreach (var program in OpenWindows.List().Where(w => w.ExePath is not null).GroupBy(w => System.IO.Path.GetFileName(w.ExePath!).ToLowerInvariant()))
            apps.Items.Add(new ComboBoxItem { Content = program.First().AppName.Length > 0 ? program.First().AppName : program.Key, Tag = program.Key });
        apps.SelectedIndex = apps.Items.Count > 0 ? 0 : -1;
        var appRow = new StackPanel { Orientation = Orientation.Horizontal };
        appRow.Children.Add(apps);
        var startApp = _parts.PillButton(Strings.AwakeStart, prominent: false, () =>
        {
            if (apps.SelectedItem is ComboBoxItem { Tag: string exe, Content: string name }) _awake.Begin(AwakeSession.While(exe, name));
        });
        startApp.Margin = new Thickness(8, 0, 0, 0);
        appRow.Children.Add(startApp);

        Children.Add(_parts.Group(null, null,
            _parts.Row(Strings.AwakeUntilTime, null, untilRow),
            _parts.Row(Strings.AwakeWhileApp, Strings.AwakeWhileAppDetail, appRow),
            _parts.Row(Strings.AwakeKeepScreen, Strings.AwakeKeepScreenDetail, _parts.Toggle(_preferences.Get<bool>(Pref.AwakeDisplay), on => _preferences.Set(Pref.AwakeDisplay, on)))));

        Children.Add(_parts.Group(Strings.AwakeSafety, Strings.AwakeFooter,
            _parts.Row(Strings.AwakeBatteryFloor, Strings.AwakeBatteryFloorDetail, _parts.Chips<int>(
                [(0, Strings.AwakeOffChip), (10, "10%"), (20, "20%"), (30, "30%"), (50, "50%")],
                () => _preferences.Get<int>(Pref.AwakeBatteryFloor), v => _preferences.Set(Pref.AwakeBatteryFloor, v)))));

        _awake.Changed += Refresh;
        _clock.Tick += (_, _) => Refresh();
        Loaded += (_, _) => _clock.Start();
        Unloaded += (_, _) =>
        {
            _clock.Stop();
            _awake.Changed -= Refresh;
        };
        Refresh();
    }

    private void Refresh()
    {
        var session = _awake.Session;
        _status.Text = session is null ? Strings.AwakeOff : Strings.AwakeOn;
        _status.Foreground = session is null ? _palette.InkSecondary : _palette.Accent;
        _stop.Visibility = session is null ? Visibility.Collapsed : Visibility.Visible;
        if (session is null)
        {
            _detail.Text = "";
            return;
        }
        var describe = KeepAwakeFeature.Describe(session);
        _detail.Text = session.UntilUtc is { } until ? $"{describe} · {Strings.AwakeRemaining(AwakeRules.Format(until - DateTime.UtcNow))}" : describe;
    }
}
