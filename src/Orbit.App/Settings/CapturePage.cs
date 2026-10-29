using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbit.App.Design;
using Orbit.App.Features;
using Orbit.Core.Actions;
using Orbit.Core.Capture;
using Orbit.Core.Settings;

namespace Orbit.App.Settings;

/// <summary>
/// The Capture page (Orbit: FeaturePages.swift, <c>CapturePage</c>): the four captures, the recent
/// ones, what happens after a capture, where saves go, and the keys of the selection. Orbit's
/// timer, recording and scrolling rows are left out with those features.
/// </summary>
internal sealed class CapturePage : StackPanel
{
    private static readonly OrbitAction[] Captures = [OrbitAction.CaptureArea, OrbitAction.CaptureWindow, OrbitAction.CaptureScreen, OrbitAction.CaptureText];

    private readonly AppServices _services;
    private readonly Preferences _preferences;
    private readonly Palette _palette = Palette.Light;
    private readonly PageParts _parts = new(Palette.Light);

    public CapturePage(AppServices services)
    {
        _services = services;
        _preferences = services.Preferences;
        Children.Add(Header());
        Children.Add(ActionCards());
        Children.Add(Recent());
        Children.Add(Group(Strings.CaptureAfter, null,
            Row(Strings.CaptureCopyToClipboard, null, BoolToggle(Pref.CaptureAutoCopy)),
            Row(Strings.CapturePlaySound, null, BoolToggle(Pref.CaptureSound)),
            Row(Strings.CaptureKeepPreview, Strings.CaptureKeepPreviewDetail, SecondsChips()),
            Row(Strings.CapturePreviewPosition, Strings.CapturePreviewPositionDetail, CornerPicker())));
        Children.Add(Group(Strings.CaptureSaving, null, SaveFolderRow()));
        Children.Add(Group(Strings.CaptureWhileSelecting, null,
            Row(Strings.CaptureShowKeyHints, Strings.CaptureShowKeyHintsDetail, BoolToggle(Pref.CaptureKeyHints)),
            Tip("Space", Strings.TipSpaceSwitch), Tip("Enter", Strings.TipEnterScreen), Tip(Strings.KeyClick, Strings.TipClickWindow),
            Tip("Tab", Strings.TipTabRatio), Tip("R", Strings.TipRLast), Tip("Ctrl C", Strings.TipCopyColor)));
        Children.Add(Group(Strings.CaptureWhileDragging, null,
            Tip("Shift", Strings.TipShiftRatio), Tip("Alt", Strings.TipAltCenter), Tip("Space", Strings.TipSpaceMove), Tip("←↑↓→", Strings.TipArrows)));
        Children.Add(Group(Strings.CaptureBeforeRelease, Strings.CaptureBeforeReleaseFooter,
            Tip("C", Strings.TipCopyOnly), Tip("S", Strings.TipSave), Tip("E", Strings.TipEdit), Tip("P", Strings.TipPin), Tip("T", Strings.TipText)));
    }

    private UIElement Header()
    {
        var titles = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
        titles.Children.Add(new TextBlock { Text = Strings.PageCapture, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink });
        titles.Children.Add(_parts.Caption(Strings.CapturePageSubtitle));
        return titles;
    }

    /// <summary>The four captures as cards with their keys; a click runs one.</summary>
    private UIElement ActionCards()
    {
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 28) };
        foreach (var action in Captures)
        {
            var body = new StackPanel();
            body.Children.Add(new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Background = _palette.Ink, HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = Glyphs.Of(action), FontFamily = Glyphs.Font, FontSize = 15, Foreground = _palette.Canvas, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            body.Children.Add(new TextBlock { Text = action.Title(), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink, Margin = new Thickness(0, 10, 0, 6), TextTrimming = TextTrimming.CharacterEllipsis });
            if (_services.ShortcutFor(action) is { } hotkey)
            {
                var keys = KeyCap.Row(hotkey.Labels(), _palette);
                keys.HorizontalAlignment = HorizontalAlignment.Left;
                keys.Margin = new Thickness(-4, 0, 0, 0);
                body.Children.Add(keys);
            }
            var card = _parts.Card(body, 14);
            card.Margin = new Thickness(grid.Children.Count == 0 ? 0 : 6, 0, grid.Children.Count == Captures.Length - 1 ? 0 : 6, 0);
            card.Cursor = Cursors.Hand;
            card.ToolTip = action.Title();
            var run = action;
            card.MouseLeftButtonUp += (_, _) => _services.Actions.Perform(run);
            card.MouseEnter += (_, _) => card.Background = _palette.Raised;
            card.MouseLeave += (_, _) => card.Background = _palette.Surface;
            grid.Children.Add(card);
        }
        return grid;
    }

    /// <summary>The newest captures in a strip, and the library window behind "Show all".</summary>
    private UIElement Recent()
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 28) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var all = _parts.PillButton(Strings.CaptureShowAll, prominent: false, () => _services.Actions.Perform(OrbitAction.RecentCaptures));
        DockPanel.SetDock(all, Dock.Right);
        head.Children.Add(all);
        head.Children.Add(_parts.SectionTitle(Strings.CaptureRecent));
        section.Children.Add(head);

        var strip = new UniformGrid { Columns = 6, Rows = 1 };
        var recent = _services.CaptureCards?.Actions.Store.Recent.Take(6).ToList() ?? [];
        foreach (var path in recent) strip.Children.Add(Thumbnail(path));
        if (recent.Count == 0) section.Children.Add(_parts.Caption(Strings.LibraryEmptyDetail));
        else section.Children.Add(strip);
        return section;
    }

    private FrameworkElement Thumbnail(string path)
    {
        var image = new Image { Stretch = Stretch.UniformToFill, Height = 64 };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 240;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            image.Source = bitmap;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
        }
        var frame = new Border
        {
            CornerRadius = new CornerRadius(8), ClipToBounds = true, Background = _palette.Raised, BorderBrush = _palette.Hairline, BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 8, 0), Child = image, Cursor = Cursors.Hand, ToolTip = Path.GetFileName(path),
        };
        frame.MouseLeftButtonUp += (_, _) =>
        {
            if (_services.CaptureCards is { } cards && Features.Capture.CaptureItem.Load(path) is { } item) cards.Edit?.Invoke(item);
        };
        return frame;
    }

    // MARK: Rows

    private UIElement Group(string title, string? footer, params UIElement[] rows)
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 28) };
        section.Children.Add(_parts.SectionTitle(title, bottom: 10));
        var body = new StackPanel();
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0) body.Children.Add(new Border { Height = 1, Background = _palette.Hairline, Margin = new Thickness(0, 12, 0, 12) });
            body.Children.Add(rows[i]);
        }
        section.Children.Add(_parts.Card(body));
        if (footer is not null) section.Children.Add(_parts.Caption(footer));
        return section;
    }

    private UIElement Row(string title, string? detail, FrameworkElement control)
    {
        var row = new DockPanel();
        control.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(control, Dock.Right);
        row.Children.Add(control);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink, TextWrapping = TextWrapping.Wrap });
        if (detail is not null) text.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = _palette.InkSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(text);
        return row;
    }

    private UIElement Tip(string keys, string text) => Row(text, null, KeyCap.Row(keys.Split(' '), _palette));

    private CheckBox BoolToggle(string key) => _parts.Toggle(_preferences.Get<bool>(key), on => _preferences.Set(key, on));

    /// <summary>One pill per choice; the chosen one filled.</summary>
    private FrameworkElement Chips<T>(IReadOnlyList<(T Value, string Title)> options, Func<T> current, Action<T> set)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        void Fill()
        {
            row.Children.Clear();
            foreach (var (value, title) in options)
            {
                var pill = _parts.PillButton(title, prominent: EqualityComparer<T>.Default.Equals(value, current()), () =>
                {
                    set(value);
                    Fill();
                });
                pill.Margin = new Thickness(row.Children.Count == 0 ? 0 : 6, 0, 0, 0);
                row.Children.Add(pill);
            }
        }
        Fill();
        return row;
    }

    private FrameworkElement SecondsChips() => Chips<double>(
        [(5.0, Strings.WholeSeconds(5)), (8.0, Strings.WholeSeconds(8)), (15.0, Strings.WholeSeconds(15)), (0.0, Strings.CaptureUntilClosed)],
        () => _preferences.Get<double>(Pref.CaptureThumbnailSeconds), v => _preferences.Set(Pref.CaptureThumbnailSeconds, v));

    /// <summary>The four corners as small screens with the chosen corner marked, and a button that shows a sample card there.</summary>
    private FrameworkElement CornerPicker()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var screens = new StackPanel { Orientation = Orientation.Horizontal };
        void Fill()
        {
            screens.Children.Clear();
            var chosen = CardCorner.Parse(_preferences.Get<string>(Pref.CaptureThumbnailCorner));
            foreach (var name in CardCorner.Names)
            {
                var corner = CardCorner.Parse(name);
                bool on = corner == chosen;
                var mark = new Border
                {
                    Width = 14, Height = 9, CornerRadius = new CornerRadius(2), Background = on ? _palette.Accent : _palette.InkSecondary, Opacity = on ? 1 : 0.5,
                    HorizontalAlignment = corner.OnLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    VerticalAlignment = corner.OnTop ? VerticalAlignment.Top : VerticalAlignment.Bottom, Margin = new Thickness(4),
                };
                var screen = new Border
                {
                    Width = 46, Height = 32, CornerRadius = new CornerRadius(5), Background = _palette.Canvas, Cursor = Cursors.Hand,
                    BorderBrush = on ? _palette.Accent : _palette.Hairline, BorderThickness = new Thickness(on ? 2 : 1), Child = mark,
                    Margin = new Thickness(screens.Children.Count == 0 ? 0 : 6, 0, 0, 0), ToolTip = Strings.CaptureCornerName(name),
                };
                var picked = name;
                screen.MouseLeftButtonUp += (_, _) =>
                {
                    _preferences.Set(Pref.CaptureThumbnailCorner, picked);
                    Fill();
                };
                screens.Children.Add(screen);
            }
        }
        Fill();
        row.Children.Add(screens);
        var sample = _parts.PillButton(Strings.CapturePreviewTry, prominent: false, () => _services.CaptureCards?.ShowSample());
        sample.Margin = new Thickness(12, 0, 0, 0);
        sample.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(sample);
        return row;
    }

    private UIElement SaveFolderRow()
    {
        var path = new TextBlock { FontSize = 11, Foreground = _palette.InkSecondary, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        void Show() => path.Text = _preferences.Get<string>(Pref.CaptureSaveFolder);
        Show();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_parts.PillButton(Strings.CaptureChange, prominent: false, () =>
        {
            var current = _preferences.Get<string>(Pref.CaptureSaveFolder);
            var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = Directory.Exists(current) ? current : "" };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            _preferences.Set(Pref.CaptureSaveFolder, dialog.FolderName);
            Show();
        }));
        var open = _parts.PillButton(Strings.LibraryOpenFolder, prominent: false, () =>
        {
            var folder = _preferences.Get<string>(Pref.CaptureSaveFolder);
            if (Directory.Exists(folder)) Shell.Open(folder);
            else Overlays.Toast.Show(Strings.OpenFailed);
        });
        open.Margin = new Thickness(6, 0, 0, 0);
        buttons.Children.Add(open);
        var row = new DockPanel();
        buttons.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        var text = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock { Text = Strings.CaptureSaveTo, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink });
        text.Children.Add(path);
        row.Children.Add(text);
        return row;
    }
}
