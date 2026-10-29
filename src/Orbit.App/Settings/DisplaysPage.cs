using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Orbit.App.Design;
using Orbit.App.Features;
using Orbit.App.Overlays;
using Orbit.Core.Displays;
using Orbit.Core.Settings;

namespace Orbit.App.Settings;

/// <summary>
/// The Displays page (Orbit: App/Pages/DisplaysPage.swift): a card per screen with its brightness
/// and, where the monitor answered, contrast and volume; quick levels and saved presets; which
/// screen the brightness actions change and whether the level bar shows. The screens are asked
/// again each time the page opens.
/// </summary>
internal sealed class DisplaysPage : StackPanel
{
    private readonly Palette _palette = Palette.Light;
    private readonly PageParts _parts = new(Palette.Light);
    private readonly Preferences _preferences;
    private readonly DisplayManager? _manager;
    private readonly StackPanel _cards = new() { Margin = new Thickness(0, 0, 0, 28) };
    private readonly WrapPanel _presets = new() { MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Dictionary<ManagedDisplay, Slider> _brightness = [];
    private bool _updating;

    public DisplaysPage(AppServices services)
    {
        _preferences = services.Preferences;
        _manager = services.Displays;

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
        var again = _parts.PillButton(Strings.DisplaysAskAgain, false, () => _ = _manager?.Refresh());
        again.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(again, Dock.Right);
        header.Children.Add(again);
        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = Strings.PageDisplays, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink });
        title.Children.Add(_parts.Caption(Strings.DisplaysSubtitle));
        header.Children.Add(title);
        Children.Add(header);

        _cards.Children.Add(_parts.Caption(Strings.DisplaysAsking));
        Children.Add(_cards);

        var name = new TextBox { Width = 140, FontSize = 13, Padding = new Thickness(6, 3, 6, 3), VerticalContentAlignment = VerticalAlignment.Center };
        var save = _parts.PillButton(Strings.DisplaysSave, true, () => SavePreset(name));
        save.Margin = new Thickness(8, 0, 0, 0);
        name.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) SavePreset(name);
        };
        var saveRow = new StackPanel { Orientation = Orientation.Horizontal };
        saveRow.Children.Add(name);
        saveRow.Children.Add(save);
        FillPresets();
        Children.Add(_parts.Group(Strings.DisplaysPresets, null,
            _parts.Row(Strings.DisplaysQuickLevels, Strings.DisplaysQuickLevelsHint, _presets),
            _parts.Row(Strings.DisplaysSaveCurrent, Strings.DisplaysSaveCurrentHint, saveRow)));

        Children.Add(_parts.Group(Strings.DisplaysActions, Strings.DisplaysActionsFooter,
            _parts.Row(Strings.DisplaysTarget, Strings.DisplaysTargetHint, _parts.Chips<string>(
                [("pointer", Strings.DisplaysTargetPointer), ("main", Strings.DisplaysTargetMain), ("all", Strings.DisplaysTargetAll)],
                () => _preferences.Get<string>(Pref.DisplaysTarget), v => _preferences.Set(Pref.DisplaysTarget, v))),
            _parts.Row(Strings.DisplaysHud, Strings.DisplaysHudHint,
                _parts.Toggle(_preferences.Get<bool>(Pref.DisplaysHud), on => _preferences.Set(Pref.DisplaysHud, on)))));

        if (_manager is { } manager)
        {
            Loaded += (_, _) =>
            {
                manager.Changed += FillCards;
                manager.BrightnessChanged += Follow;
                _ = manager.Refresh();
            };
            Unloaded += (_, _) =>
            {
                manager.Changed -= FillCards;
                manager.BrightnessChanged -= Follow;
            };
        }
    }

    /// <summary>A brightness action or preset moved a level: the slider follows without sending it back.</summary>
    private void Follow(ManagedDisplay display)
    {
        if (_updating || !_brightness.TryGetValue(display, out var slider)) return;
        _updating = true;
        slider.Value = Math.Round(display.Brightness * 100);
        _updating = false;
    }

    private void FillCards()
    {
        _cards.Children.Clear();
        _brightness.Clear();
        var displays = _manager?.Displays ?? [];
        foreach (var display in displays)
        {
            var card = _parts.Card(Card(display));
            card.Margin = new Thickness(0, 0, 0, 10);
            _cards.Children.Add(card);
        }
        if (!displays.Any(d => !d.IsBuiltIn))
        {
            _cards.Children.Add(new Border
            {
                BorderBrush = _palette.Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(16),
                Child = _parts.CardTitle("", Strings.DisplaysNoMonitor, Strings.DisplaysNoMonitorHint),
            });
        }
    }

    private UIElement Card(ManagedDisplay display)
    {
        var body = new StackPanel();
        string name = display.Name.Length > 0 ? display.Name : display.IsBuiltIn ? Strings.DisplaysBuiltInName : Strings.DisplaysMonitorName;
        body.Children.Add(_parts.CardTitle(display.IsBuiltIn ? "" : "", name, Detail(display)));
        if (!display.CanSetBrightness) return body;
        var manager = _manager!;
        var brightness = Level(Strings.DisplaysBrightness, display.Brightness, v => manager.SetBrightness(display, v));
        _brightness[display] = brightness.Children.OfType<Slider>().First();
        body.Children.Add(brightness);
        if (display.Kind == DisplayKind.Ddc && display.Contrast is { } contrast)
            body.Children.Add(Level(Strings.DisplaysContrast, contrast, v => manager.SetContrast(display, v)));
        if (display.Kind == DisplayKind.Ddc && display.Volume is { } volume)
        {
            var level = Level(Strings.DisplaysVolume, volume, v => manager.SetVolume(display, v));
            if (display.Muted is not null)
            {
                Button? mute = null;
                mute = _parts.PillButton(display.Muted == true ? Strings.DisplaysUnmute : Strings.DisplaysMute, false, () =>
                {
                    manager.SetMuted(display, display.Muted != true);
                    mute!.Content = display.Muted == true ? Strings.DisplaysUnmute : Strings.DisplaysMute;
                });
                mute.Margin = new Thickness(10, 0, 0, 0);
                DockPanel.SetDock(mute, Dock.Right);
                level.Children.Insert(0, mute);
            }
            body.Children.Add(level);
        }
        return body;
    }

    private static string Detail(ManagedDisplay display) => display switch
    {
        { Unreachable: true } => Strings.DisplaysUnreachable,
        { Kind: DisplayKind.BuiltIn } => Strings.DisplaysKindBuiltIn,
        { Kind: DisplayKind.Ddc } => Strings.DisplaysKindDdc,
        { Kind: DisplayKind.Duplicated } => Strings.DisplaysKindDuplicated,
        { IsBuiltIn: true } => Strings.DisplaysKindNoPanel,
        _ => Strings.DisplaysKindNoDdc,
    };

    /// <summary>A label, a 0–100 slider and its percentage; the value goes out as the slider moves.</summary>
    private DockPanel Level(string label, double value, Action<double> set)
    {
        var row = new DockPanel { Margin = new Thickness(44, 12, 0, 0) };
        var text = new TextBlock { Text = label, FontSize = 12, Foreground = _palette.InkSecondary, Width = 64, VerticalAlignment = VerticalAlignment.Center };
        var percent = new TextBlock { Text = $"{Math.Round(value * 100)}%", FontSize = 12, Foreground = _palette.InkSecondary, Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = Math.Round(value * 100), IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) =>
        {
            percent.Text = $"{Math.Round(e.NewValue)}%";
            if (_updating) return;
            _updating = true;
            set(e.NewValue / 100);
            _updating = false;
        };
        DockPanel.SetDock(text, Dock.Left);
        DockPanel.SetDock(percent, Dock.Right);
        row.Children.Add(text);
        row.Children.Add(percent);
        row.Children.Add(slider);
        return row;
    }

    private void FillPresets()
    {
        _presets.Children.Clear();
        void Add(string title, Action apply, ContextMenu? menu = null)
        {
            var pill = _parts.PillButton(title, false, apply);
            pill.Margin = new Thickness(6, 0, 0, 6);
            pill.ContextMenu = menu;
            _presets.Children.Add(pill);
        }
        Add(Strings.DisplaysBright, () => SetAll(1));
        Add(Strings.DisplaysComfort, () => SetAll(0.6));
        Add(Strings.DisplaysDim, () => SetAll(0.2));
        foreach (var preset in DisplayLevels.ReadPresets(_preferences.Get<string>(Pref.DisplaysPresets)))
        {
            var menu = new ContextMenu();
            menu.Items.Add(Design.Controls.MenuItem(Strings.DisplaysDeletePreset, () =>
            {
                var kept = DisplayLevels.ReadPresets(_preferences.Get<string>(Pref.DisplaysPresets)).Where(p => p.Name != preset.Name);
                _preferences.Set(Pref.DisplaysPresets, DisplayLevels.WritePresets(kept));
                FillPresets();
            }));
            Add(preset.Name, () => Apply(preset), menu);
        }
    }

    private void SetAll(double level)
    {
        if (_manager is null) return;
        var changed = _manager.Displays.Where(d => d.CanSetBrightness).ToList();
        foreach (var display in changed) _manager.SetBrightness(display, level);
        Done(changed, null);
    }

    private void Apply(BrightnessPreset preset)
    {
        if (_manager is null) return;
        // Screens the preset doesn't know are left alone.
        var changed = _manager.Displays.Where(d => d.CanSetBrightness && preset.Levels.ContainsKey(d.Key)).ToList();
        foreach (var display in changed) _manager.SetBrightness(display, preset.Levels[display.Key]);
        Done(changed, preset.Name);
    }

    private void Done(List<ManagedDisplay> changed, string? preset)
    {
        if (changed.Count == 0)
        {
            Toast.Show(Strings.DisplaysNothingToSet);
            return;
        }
        if (preset is not null) Toast.Show(Strings.DisplaysPresetApplied, preset);
        else DisplaysFeature.ShowLevel(_preferences, changed);
    }

    private void SavePreset(TextBox name)
    {
        var title = name.Text.Trim();
        if (title.Length == 0 || _manager is null) return;
        var levels = _manager.Displays.Where(d => d.CanSetBrightness).ToDictionary(d => d.Key, d => d.Brightness);
        var presets = DisplayLevels.ReadPresets(_preferences.Get<string>(Pref.DisplaysPresets)).Where(p => p.Name != title).ToList();
        presets.Add(new BrightnessPreset(title, levels));
        _preferences.Set(Pref.DisplaysPresets, DisplayLevels.WritePresets(presets));
        name.Text = "";
        FillPresets();
    }
}
