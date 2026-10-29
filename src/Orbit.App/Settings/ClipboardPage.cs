using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Orbit.App.Design;
using Orbit.App.Features;
using Orbit.Core.Actions;
using Orbit.Core.Clipboard;
using Orbit.Core.Settings;

namespace Orbit.App.Settings;

/// <summary>
/// The Clipboard page (Orbit: FeaturePages.swift, <c>ClipboardPage</c>): what the history holds,
/// recording and its pause, pasting, how much to keep, clearing, and programs never recorded.
/// Orbit's Shelf rows are left out with the Shelf.
/// </summary>
internal sealed class ClipboardPage : StackPanel
{
    private readonly AppServices _services;
    private readonly Preferences _preferences;
    private readonly Palette _palette = Palette.Light;
    private readonly PageParts _parts = new(Palette.Light);
    private readonly StackPanel _recording = new();
    private readonly StackPanel _ignored = new();
    private readonly StackPanel _cleanUrl = new();

    public ClipboardPage(AppServices services)
    {
        _services = services;
        _preferences = services.Preferences;
        Children.Add(Header());
        Children.Add(Counts());
        FillRecording();
        Children.Add(_parts.Group(null, null,
            _recording,
            _parts.Row(Strings.ClipPasteDirectly, Strings.ClipPasteDirectlyDetail, _parts.Toggle(_preferences.Get<bool>(Pref.PasteDirectly), on => _preferences.Set(Pref.PasteDirectly, on))),
            _parts.Row(Strings.ClipKeep, Strings.ClipKeepDetail, _parts.Chips<int>([(100, "100"), (500, "500"), (1000, "1K"), (5000, "5K")],
                () => _preferences.Get<int>(Pref.HistoryLimit), v =>
                {
                    _preferences.Set(Pref.HistoryLimit, v);
                    _services.ClipboardHistory?.History.Trim();
                })),
            _parts.Row(Strings.ClipClear, Strings.ClipClearDetail, _parts.PillButton(Strings.ClipClearButton, prominent: false, Clear))));
        FillCleanUrl();
        Children.Add(_parts.Group(Strings.CleanUrlTitle, Strings.CleanUrlFooter, _cleanUrl));
        FillIgnored();
        Children.Add(_parts.Group(Strings.ClipNeverRecord, Strings.ClipNeverRecordFooter, _ignored));
    }

    private UIElement Header()
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
        var open = _parts.PillButton(Strings.ClipOpenHistory, prominent: true, () => _services.Actions.Perform(OrbitAction.Clipboard));
        open.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(open, Dock.Right);
        header.Children.Add(open);
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = Strings.PageClipboard, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink });
        titles.Children.Add(_parts.Caption(Strings.ClipPageSubtitle));
        header.Children.Add(titles);
        return header;
    }

    private UIElement Counts()
    {
        var items = _services.ClipboardHistory?.History.Items ?? [];
        var grid = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 28) };
        var kinds = new (string Glyph, string Title, int Count)[]
        {
            ("", Strings.ClipKindName(ClipKind.Text), items.Count(i => i.Kind == ClipKind.Text)),
            ("", Strings.ClipKindName(ClipKind.Link), items.Count(i => i.Kind == ClipKind.Link)),
            ("", Strings.ClipKindName(ClipKind.Image), items.Count(i => i.Kind == ClipKind.Image)),
            (Glyphs.Document, Strings.ClipKindName(ClipKind.File), items.Count(i => i.Kind == ClipKind.File)),
            ("", Strings.ClipPinned, items.Count(i => i.Pinned)),
        };
        foreach (var (glyph, title, count) in kinds)
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = glyph, FontFamily = Glyphs.Font, FontSize = 13, Foreground = _palette.InkSecondary });
            body.Children.Add(new TextBlock { Text = count.ToString(), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = _palette.Ink, Margin = new Thickness(0, 8, 0, 0) });
            body.Children.Add(new TextBlock { Text = title, FontSize = 11, Foreground = _palette.InkSecondary });
            var card = _parts.Card(body, 14);
            card.Margin = new Thickness(grid.Children.Count == 0 ? 0 : 6, 0, grid.Children.Count == kinds.Length - 1 ? 0 : 6, 0);
            grid.Children.Add(card);
        }
        return grid;
    }

    private void FillRecording()
    {
        _recording.Children.Clear();
        var history = _services.ClipboardHistory;
        bool paused = history?.Paused ?? false;
        FrameworkElement control;
        if (paused)
        {
            control = _parts.PillButton(Strings.ClipResume, prominent: true, () =>
            {
                history?.Resume();
                FillRecording();
            });
        }
        else
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (title, span) in new (string, TimeSpan?)[] { (Strings.ClipPause15, TimeSpan.FromMinutes(15)), (Strings.ClipPause60, TimeSpan.FromHours(1)), (Strings.ClipPauseUntilResumed, null) })
            {
                var pill = _parts.PillButton(title, prominent: false, () =>
                {
                    history?.Pause(span);
                    FillRecording();
                });
                pill.Margin = new Thickness(row.Children.Count == 0 ? 0 : 6, 0, 0, 0);
                row.Children.Add(pill);
            }
            control = row;
        }
        _recording.Children.Add(_parts.Row(paused ? Strings.ClipRecordingPaused : Strings.ClipRecording,
            Strings.ClipStoredHere(history?.History.Items.Count ?? 0), control));
    }

    private void Clear()
    {
        if (MessageBox.Show(Window.GetWindow(this)!, Strings.ClipClearConfirm, Strings.ClipClear, MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        _services.ClipboardHistory?.History.Clear();
        FillRecording();
    }

    /// <summary>Clean URL: the automatic switch, what it last removed, the built-in names and the user's own.</summary>
    private void FillCleanUrl()
    {
        _cleanUrl.Children.Clear();
        void Separator() => _cleanUrl.Children.Add(new Border { Height = 1, Background = _palette.Hairline, Margin = new Thickness(0, 12, 0, 12) });
        _cleanUrl.Children.Add(_parts.Row(Strings.CleanUrlAuto, Strings.CleanUrlAutoDetail,
            _parts.Toggle(_preferences.Get<bool>(Pref.CleanUrlAuto), on => _preferences.Set(Pref.CleanUrlAuto, on))));
        Separator();
        var last = _preferences.Get<string[]>(Pref.CleanUrlLastRemoved);
        _cleanUrl.Children.Add(_parts.Row(Strings.CleanUrlLast, last.Length == 0 ? Strings.CleanUrlNone : string.Join(", ", last), new Border()));
        Separator();

        var disabled = _preferences.Get<string[]>(Pref.CleanUrlDisabled).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var all = new[] { "utm_*" }.Concat(UrlCleaner.BuiltInNames).Concat(UrlCleaner.BuiltInHostRules.Select(r => r.Name)).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var name in all)
        {
            var site = UrlCleaner.BuiltInHostRules.FirstOrDefault(r => r.Name == name);
            var chip = _parts.PillButton(site is null ? name : $"{name} ({string.Join(", ", site.Hosts)})", prominent: !disabled.Contains(name), () =>
            {
                var off = _preferences.Get<string[]>(Pref.CleanUrlDisabled).ToList();
                if (!off.Remove(name)) off.Add(name);
                _preferences.Set(Pref.CleanUrlDisabled, off.ToArray());
                FillCleanUrl();
            });
            chip.Margin = new Thickness(0, 0, 6, 6);
            names.Children.Add(chip);
        }
        var builtIn = new StackPanel();
        builtIn.Children.Add(new TextBlock { Text = Strings.CleanUrlBuiltIn, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink });
        builtIn.Children.Add(new TextBlock { Text = Strings.CleanUrlBuiltInDetail, FontSize = 11, Foreground = _palette.InkSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        builtIn.Children.Add(names);
        _cleanUrl.Children.Add(builtIn);
        Separator();

        var added = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var name in _preferences.Get<string[]>(Pref.CleanUrlAdded))
        {
            var chip = _parts.PillButton($"{name}  ×", prominent: true, () =>
            {
                _preferences.Set(Pref.CleanUrlAdded, _preferences.Get<string[]>(Pref.CleanUrlAdded).Where(n => n != name).ToArray());
                FillCleanUrl();
            });
            chip.ToolTip = Strings.ClipRemove;
            chip.Margin = new Thickness(0, 0, 6, 6);
            added.Children.Add(chip);
        }
        // Transparent, so the hint behind it shows while it is empty.
        var field = new TextBox { Width = 180, FontSize = 13, Padding = new Thickness(6, 3, 6, 3), VerticalContentAlignment = VerticalAlignment.Center, Background = System.Windows.Media.Brushes.Transparent };
        void Add()
        {
            var name = field.Text.Trim();
            if (name.Length == 0) return;
            var current = _preferences.Get<string[]>(Pref.CleanUrlAdded);
            if (!current.Contains(name, StringComparer.OrdinalIgnoreCase)) _preferences.Set(Pref.CleanUrlAdded, current.Append(name).ToArray());
            FillCleanUrl();
        }
        field.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Add();
        };
        var addRow = new StackPanel { Orientation = Orientation.Horizontal };
        addRow.Children.Add(_palette.WithHint(field, Strings.CleanUrlAddHint, 8));
        var add = _parts.PillButton(Strings.CleanUrlAdd, prominent: false, Add);
        add.Margin = new Thickness(8, 0, 0, 0);
        addRow.Children.Add(add);
        var mine = new StackPanel();
        mine.Children.Add(new TextBlock { Text = Strings.CleanUrlAdded, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink });
        mine.Children.Add(added);
        mine.Children.Add(addRow);
        _cleanUrl.Children.Add(mine);
    }

    private void FillIgnored()
    {
        _ignored.Children.Clear();
        var list = _preferences.Get<string[]>(Pref.IgnoredApps);
        foreach (var process in list)
        {
            var name = process;
            var remove = _parts.PillButton(Strings.ClipRemove, prominent: false, () =>
            {
                _preferences.Set(Pref.IgnoredApps, _preferences.Get<string[]>(Pref.IgnoredApps).Where(p => p != name).ToArray());
                FillIgnored();
            });
            _ignored.Children.Add(_parts.Row(name, null, remove));
            _ignored.Children.Add(new Border { Height = 1, Background = _palette.Hairline, Margin = new Thickness(0, 12, 0, 12) });
        }
        _ignored.Children.Add(_parts.Row(Strings.ClipAddApp, null, _parts.PillButton(Strings.ClipChoose, prominent: false, () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = $"{Strings.ClipPrograms}|*.exe",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            var file = Path.GetFileName(dialog.FileName).ToLowerInvariant();
            var current = _preferences.Get<string[]>(Pref.IgnoredApps);
            if (!current.Contains(file)) _preferences.Set(Pref.IgnoredApps, current.Append(file).ToArray());
            FillIgnored();
        })));
    }
}
