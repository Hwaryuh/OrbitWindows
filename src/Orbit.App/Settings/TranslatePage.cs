using System.Windows;
using System.Windows.Controls;
using Orbit.App.Design;
using Orbit.App.Features;
using Orbit.App.Overlays;
using Orbit.Core.Actions;
using Orbit.Core.Settings;
using Orbit.Core.Translate;

namespace Orbit.App.Settings;

/// <summary>
/// The Translate page (Orbit: FeaturePages.swift, <c>TranslatePage</c>): Ctrl+C twice and its
/// window, the engine and the DeepL key, the two languages, formality, and how the card looks.
/// </summary>
internal sealed class TranslatePage : StackPanel
{
    private readonly AppServices _services;
    private readonly Preferences _preferences;
    private readonly Palette _palette = Palette.Light;
    private readonly PageParts _parts = new(Palette.Light);
    private readonly StackPanel _engine = new();

    public TranslatePage(AppServices services)
    {
        _services = services;
        _preferences = services.Preferences;
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(new TextBlock { Text = Strings.PageTranslate, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink });
        header.Children.Add(_parts.Caption(Strings.TranslatePageSubtitle));
        Children.Add(header);

        var keys = services.ShortcutFor(OrbitAction.Translate) is { } hotkey ? KeyCap.Row(hotkey.Labels(), _palette) : new StackPanel();
        Children.Add(_parts.Group(null, null,
            _parts.Row(Strings.TranslateDoubleCopy, Strings.TranslateDoubleCopyDetail, BoolToggle(Pref.TranslateDoubleCopy)),
            _parts.Row(Strings.TranslateInterval, null, _parts.Chips<double>(
                [(0.3, Strings.ShortSeconds(0.3)), (0.4, Strings.ShortSeconds(0.4)), (0.5, Strings.ShortSeconds(0.5)), (0.6, Strings.ShortSeconds(0.6))],
                () => _preferences.Get<double>(Pref.TranslateInterval), v => _preferences.Set(Pref.TranslateInterval, v))),
            _parts.Row(OrbitAction.Translate.Title(), null, keys)));

        FillEngine();
        Children.Add(_parts.Group(Strings.TranslateEngineTitle, null, _engine));

        Children.Add(_parts.Group(null, null,
            _parts.Row(Strings.TranslatePrimary, Strings.TranslatePrimaryDetail, LanguagePicker(Pref.TranslatePrimary)),
            _parts.Row(Strings.TranslateSecondary, null, LanguagePicker(Pref.TranslateSecondary)),
            _parts.Row(Strings.TranslateFormality, null, _parts.Chips<string>(
                Enum.GetValues<Formality>().Select(f => (f.ToString().ToLowerInvariant(), Strings.TranslateFormalityName(f))).ToList(),
                () => _preferences.Get<string>(Pref.TranslateFormality), v => _preferences.Set(Pref.TranslateFormality, v)))));

        Children.Add(_parts.Group(null, null,
            _parts.Row(Strings.TranslateFontSize, null, FontSlider()),
            _parts.Row(Strings.TranslateShowSource, null, BoolToggle(Pref.TranslateShowSource)),
            _parts.Row(Strings.TranslateCloseOnBlur, null, BoolToggle(Pref.TranslateCloseOnBlur))));
    }

    private CheckBox BoolToggle(string key) => _parts.Toggle(_preferences.Get<bool>(key), on => _preferences.Set(key, on));

    private void FillEngine()
    {
        _engine.Children.Clear();
        bool deepl = string.Equals(_preferences.Get<string>(Pref.TranslateEngine), "deepl", StringComparison.OrdinalIgnoreCase);
        _engine.Children.Add(_parts.Row(deepl ? "DeepL" : "Google", deepl ? Strings.TranslateDeepLDetail : Strings.TranslateGoogleDetail,
            _parts.Chips<string>([("google", "Google"), ("deepl", "DeepL")], () => _preferences.Get<string>(Pref.TranslateEngine).ToLowerInvariant(), v =>
            {
                _preferences.Set(Pref.TranslateEngine, v);
                FillEngine();
            })));
        if (!deepl) return;
        _engine.Children.Add(new Border { Height = 1, Background = _palette.Hairline, Margin = new Thickness(0, 12, 0, 12) });
        var hasKey = Secrets.Unprotect(_preferences.Get<string>(Pref.TranslateDeepLKey)).Length > 0;
        var box = new PasswordBox { Width = 220, FontSize = 13, Padding = new Thickness(6, 3, 6, 3), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = Strings.TranslateDeepLKeyHint };
        var save = _parts.PillButton(Strings.Save, prominent: true, () =>
        {
            if (box.Password.Trim().Length == 0) return;
            _preferences.Set(Pref.TranslateDeepLKey, Secrets.Protect(box.Password.Trim()));
            box.Clear();
            Toast.Show(Strings.TranslateDeepLKeySaved);
            FillEngine();
        });
        save.Margin = new Thickness(8, 0, 0, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        row.Children.Add(save);
        if (hasKey)
        {
            var remove = _parts.PillButton(Strings.Delete, prominent: false, () =>
            {
                _preferences.Set(Pref.TranslateDeepLKey, "");
                Toast.Show(Strings.TranslateDeepLKeyRemoved);
                FillEngine();
            });
            remove.Margin = new Thickness(6, 0, 0, 0);
            row.Children.Add(remove);
        }
        _engine.Children.Add(_parts.Row(Strings.TranslateDeepLKey, Strings.TranslateDeepLKeyDetail + (hasKey ? " ✓" : ""), row));
    }

    private ComboBox LanguagePicker(string key)
    {
        var box = new ComboBox { Width = 180, FontSize = 13 };
        foreach (var language in Languages.All) box.Items.Add(new ComboBoxItem { Content = language.Name, Tag = language.Code });
        var current = _preferences.Get<string>(key);
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == current);
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: string code }) _preferences.Set(key, code);
        };
        return box;
    }

    private FrameworkElement FontSlider()
    {
        double size = Math.Clamp(_preferences.Get<double>(Pref.TranslateFontSize), 12, 24);
        var value = new TextBlock { Text = $"{size:0}", FontSize = 12, Foreground = _palette.InkSecondary, Width = 28, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var slider = new Slider { Minimum = 12, Maximum = 24, TickFrequency = 1, IsSnapToTickEnabled = true, Value = size, Width = 160, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) =>
        {
            value.Text = $"{e.NewValue:0}";
            _preferences.Set(Pref.TranslateFontSize, e.NewValue);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(slider);
        row.Children.Add(value);
        return row;
    }
}
