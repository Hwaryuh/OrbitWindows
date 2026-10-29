using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbit.App.Design;
using Orbit.App.Features.Capture.Editor;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Capture;
using Orbit.Core.Display;
using Orbit.Core.Settings;

namespace Orbit.App.Features.Capture;

/// <summary>
/// Every recent capture in one window (Orbit: CaptureLibrary.swift): drag one into another app to
/// attach it, copy, edit, open, show in File Explorer or throw away. Arrows move the selection,
/// Enter edits, Space opens, Delete throws away, Ctrl+C copies, Esc closes. One window at a time.
/// </summary>
internal sealed class LibraryWindow : Window
{
    private const int Columns = 3;
    private const double TileHeight = 104;

    private static LibraryWindow? _open;
    /// <summary>Thumbnails by file and its write time, shared between openings.</summary>
    private static readonly Dictionary<(string, DateTime), BitmapSource> Thumbnails = [];

    private readonly QuickAccess _cards;
    private readonly Preferences _preferences;
    private readonly Palette _palette = Palette.Light;
    private readonly UniformGrid _grid = new() { Columns = Columns, VerticalAlignment = VerticalAlignment.Top };
    private readonly ScrollViewer _scroll;
    private readonly Grid _body = new();
    private readonly FrameworkElement _empty;
    private readonly List<(LibraryEntry Entry, Border Frame)> _tiles = [];
    private string? _selection;
    private bool _closed;

    private LibraryWindow(QuickAccess cards, Preferences preferences)
    {
        _cards = cards;
        _preferences = preferences;
        Title = Strings.LibraryTitle;
        Width = 560;
        Height = 600;
        MinWidth = 420;
        MinHeight = 320;
        Topmost = true;
        ShowInTaskbar = true;
        Background = _palette.Canvas;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Malgun Gothic");

        _scroll = new ScrollViewer { Content = _grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16), Focusable = false };
        _empty = Empty();
        _body.Children.Add(_scroll);
        _body.Children.Add(_empty);

        var root = new DockPanel();
        var header = Header();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var footer = Footer();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(_body);
        Content = root;

        PreviewKeyDown += KeyPressed;
        Closed += (_, _) =>
        {
            _closed = true;
            if (_open == this) _open = null;
        };
    }

    /// <summary>Shows the window (reloaded), centered on the monitor under the pointer, or brings it to the front.</summary>
    public static void ShowOrActivate(QuickAccess cards, Preferences preferences)
    {
        bool fresh = _open is null;
        _open ??= new LibraryWindow(cards, preferences);
        _open.Reload();
        if (fresh && WindowPlacement.PointerMonitor() is { } monitor) WindowPlacement.ShowAt(_open, WindowPlacement.Centered(monitor, 560, 600));
        _open.Show();
        if (_open.WindowState == WindowState.Minimized) _open.WindowState = WindowState.Normal;
        _open.Activate();
    }

    // MARK: Layout

    private FrameworkElement Header()
    {
        var bar = new DockPanel { Height = 52, Margin = new Thickness(16, 0, 12, 0) };
        var close = new Button
        {
            Content = new TextBlock { Text = Glyphs.Close, FontFamily = Glyphs.Font, FontSize = 9, Foreground = _palette.InkSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Width = 22, Height = 22, Background = _palette.Raised, BorderThickness = new Thickness(0), Template = Controls.RoundTemplate(11), Cursor = Cursors.Hand,
            ToolTip = Strings.LibraryCloseHint, Focusable = false, VerticalAlignment = VerticalAlignment.Center,
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        bar.Children.Add(close);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "", FontFamily = Glyphs.Font, FontSize = 14, Foreground = _palette.Accent, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(new TextBlock { Text = Strings.LibraryTitle, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = _palette.Ink, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(title);
        return new Border { BorderBrush = _palette.Hairline, BorderThickness = new Thickness(0, 0, 0, 1), Child = bar };
    }

    private FrameworkElement Footer()
    {
        var bar = new DockPanel { Height = 38, Margin = new Thickness(16, 0, 16, 0) };
        var open = new Button
        {
            Content = new TextBlock { Text = Strings.LibraryOpenFolder, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink },
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Template = Controls.RoundTemplate(0), Cursor = Cursors.Hand,
            ToolTip = Strings.LibraryOpenFolderHint, Focusable = false, VerticalAlignment = VerticalAlignment.Center,
        };
        open.Click += (_, _) => OpenFolder(_cards.Actions.Store.Folder);
        DockPanel.SetDock(open, Dock.Right);
        bar.Children.Add(open);
        bar.Children.Add(new TextBlock { Text = Strings.LibraryHint, FontSize = 11, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        return new Border { BorderBrush = _palette.Hairline, BorderThickness = new Thickness(0, 1, 0, 0), Child = bar };
    }

    private FrameworkElement Empty()
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        panel.Children.Add(new TextBlock { Text = "", FontFamily = Glyphs.Font, FontSize = 28, Foreground = _palette.InkSecondary, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = Strings.LibraryEmpty, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(new TextBlock { Text = Strings.LibraryEmptyDetail, FontSize = 12, Foreground = _palette.InkSecondary, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
        return panel;
    }

    // MARK: Tiles

    private void Reload()
    {
        var entries = CaptureLibrary.List(_cards.Actions.Store.Folder, _preferences.Get<string>(Pref.CaptureSaveFolder));
        // Only listed files keep a thumbnail, so the cache stays within the list's 200.
        var listed = entries.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in Thumbnails.Keys.Where(k => !listed.Contains(k.Item1)).ToList()) Thumbnails.Remove(key);
        _grid.Children.Clear();
        _tiles.Clear();
        foreach (var entry in entries) _grid.Children.Add(Tile(entry));
        if (_selection is not null && entries.All(e => e.Path != _selection)) _selection = null;
        _empty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PaintSelection();
    }

    private FrameworkElement Tile(LibraryEntry entry)
    {
        var picture = new Grid { Height = TileHeight, Background = _palette.Raised, ClipToBounds = true };
        var image = new System.Windows.Controls.Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        picture.Children.Add(image);
        _ = LoadThumbnailAsync(entry.Path, image);

        var dim = new Border { Background = Palette.Fade(Brushes.Black, 0.28), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        picture.Children.Add(dim);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        actions.Children.Add(TileButton("", Strings.CardEdit, () => Edit(entry)));
        actions.Children.Add(TileButton(Glyphs.Copy, Strings.ActionCopy, () => Copy(entry)));
        actions.Children.Add(TileButton("", Strings.LibraryOpen, () => OpenFile(entry)));
        actions.Children.Add(TileButton(Glyphs.Folder, Strings.CardShowInExplorer, () => Reveal(entry)));
        actions.Children.Add(TileButton("", Strings.CardMoveToRecycleBin, () => Trash(entry)));
        picture.Children.Add(actions);

        var frame = new Border
        {
            CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = _palette.Hairline, Child = picture, Cursor = Cursors.Hand,
        };
        picture.Clip = null;
        frame.Loaded += (_, _) => picture.Clip = new RectangleGeometry(new Rect(0, 0, picture.ActualWidth, picture.ActualHeight), 9, 9);
        frame.SizeChanged += (_, e) => picture.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, e.NewSize.Width - 2), Math.Max(0, e.NewSize.Height - 2)), 9, 9);
        frame.MouseEnter += (_, _) =>
        {
            dim.Visibility = Visibility.Visible;
            actions.Visibility = Visibility.Visible;
        };
        frame.MouseLeave += (_, _) =>
        {
            dim.Visibility = Visibility.Collapsed;
            actions.Visibility = Visibility.Collapsed;
        };
        Point? press = null;
        frame.MouseLeftButtonDown += (_, e) =>
        {
            Select(entry);
            if (e.ClickCount == 2)
            {
                Edit(entry);
                return;
            }
            press = e.GetPosition(this);
        };
        frame.MouseMove += (_, e) =>
        {
            if (press is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            press = null;
            if (File.Exists(entry.Path)) DragDrop.DoDragDrop(frame, new DataObject(DataFormats.FileDrop, new[] { entry.Path }), DragDropEffects.Copy);
        };
        frame.MouseLeftButtonUp += (_, _) => press = null;
        frame.ContextMenu = Menu(entry);

        var tile = new StackPanel { Margin = new Thickness(6, 0, 6, 14) };
        tile.Children.Add(frame);
        tile.Children.Add(new TextBlock
        {
            Text = Strings.CapturedWhen(entry.Taken, DateTime.Now), FontSize = 10, FontWeight = FontWeights.Medium, Foreground = _palette.InkSecondary,
            Margin = new Thickness(2, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
        });
        _tiles.Add((entry, frame));
        return tile;
    }

    private ContextMenu Menu(LibraryEntry entry)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Controls.MenuItem(Strings.CardEdit, () => Edit(entry)));
        menu.Items.Add(Controls.MenuItem(Strings.ActionCopy, () => Copy(entry)));
        menu.Items.Add(Controls.MenuItem(Strings.LibraryOpen, () => OpenFile(entry)));
        menu.Items.Add(Controls.MenuItem(Strings.CardPin, () => Pin(entry)));
        menu.Items.Add(Controls.MenuItem(Strings.CardShowInExplorer, () => Reveal(entry)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Controls.MenuItem(Strings.CardMoveToRecycleBin, () => Trash(entry)));
        return menu;
    }

    private static Button TileButton(string glyph, string help, Action click)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = Glyphs.Font, FontSize = 10, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Width = 24, Height = 24, Margin = new Thickness(3, 0, 3, 0), Background = Palette.Fade(Brushes.White, 0.25), BorderThickness = new Thickness(0),
            Template = Controls.RoundTemplate(12), Cursor = Cursors.Hand, ToolTip = help, Focusable = false,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, help);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A small copy of the picture, read off the UI thread with the file closed again at once.</summary>
    private async Task LoadThumbnailAsync(string path, System.Windows.Controls.Image target)
    {
        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }
        if (Thumbnails.TryGetValue((path, written), out var cached))
        {
            target.Source = cached;
            return;
        }
        var thumbnail = await Task.Run(() => Decode(path));
        if (thumbnail is null || _closed) return;
        // A changed file's older thumbnail goes.
        foreach (var key in Thumbnails.Keys.Where(k => k.Item1 == path).ToList()) Thumbnails.Remove(key);
        Thumbnails[(path, written)] = thumbnail;
        target.Source = thumbnail;
    }

    private static BitmapSource? Decode(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 480;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    // MARK: Selection and keys

    private void Select(LibraryEntry entry)
    {
        _selection = entry.Path;
        PaintSelection();
    }

    private void PaintSelection()
    {
        foreach (var (entry, frame) in _tiles)
        {
            bool on = entry.Path == _selection;
            frame.BorderBrush = on ? _palette.Accent : _palette.Hairline;
            frame.BorderThickness = new Thickness(on ? 2.5 : 1);
        }
    }

    private LibraryEntry? Selected => _tiles.FirstOrDefault(t => t.Entry.Path == _selection).Entry;

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var selected = Selected;
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.C when ctrl && selected is not null:
                Copy(selected);
                break;
            case Key.Enter when selected is not null:
                Edit(selected);
                break;
            case Key.Space when selected is not null:
                OpenFile(selected);
                break;
            case Key.Delete or Key.Back when selected is not null:
                Trash(selected);
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down when _tiles.Count > 0:
                int current = _tiles.FindIndex(t => t.Entry.Path == _selection);
                int next = current < 0 ? 0 : e.Key switch
                {
                    Key.Left => current - 1,
                    Key.Right => current + 1,
                    Key.Down => current + Columns,
                    _ => current - Columns,
                };
                next = Math.Clamp(next, 0, _tiles.Count - 1);
                Select(_tiles[next].Entry);
                _tiles[next].Frame.BringIntoView();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // MARK: Actions

    private CaptureItem? Load(LibraryEntry entry)
    {
        var item = CaptureItem.Load(entry.Path);
        if (item is null) Toast.Show(Strings.CaptureFileMissing);
        return item;
    }

    private void Edit(LibraryEntry entry)
    {
        if (Load(entry) is not { } item) return;
        Close();
        EditorWindow.Open(item, _cards, _preferences);
    }

    /// <summary>The picture and the file, so it pastes into chats as an image and into File Explorer as a file.</summary>
    private void Copy(LibraryEntry entry)
    {
        if (Load(entry) is not { } item) return;
        byte[] png;
        try
        {
            png = File.ReadAllBytes(entry.Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            png = FrameImages.Png(item.Image);
        }
        var data = new DataObject();
        data.SetImage(item.Image);
        data.SetData("PNG", new MemoryStream(png));
        data.SetData(DataFormats.FileDrop, new[] { entry.Path });
        Toast.Show(Clipboards.TrySet(() => Clipboard.SetDataObject(data, copy: true)) ? Strings.CaptureCopied : Strings.CopyFailed);
    }

    private static void OpenFile(LibraryEntry entry)
    {
        if (File.Exists(entry.Path)) Shell.Open(entry.Path);
        else Toast.Show(Strings.CaptureFileMissing);
    }

    private void Pin(LibraryEntry entry)
    {
        if (Load(entry) is { } item) PinWindow.Pin(item, _cards);
    }

    private static void Reveal(LibraryEntry entry)
    {
        if (File.Exists(entry.Path)) Shell.Reveal(entry.Path);
        else Toast.Show(Strings.CaptureFileMissing);
    }

    /// <summary>Into the Recycle Bin; the selection moves to the tile that takes its place.</summary>
    private void Trash(LibraryEntry entry)
    {
        int index = _tiles.FindIndex(t => t.Entry.Path == entry.Path);
        if (!_cards.Actions.Trash(entry.Path)) return;
        Reload();
        if (_tiles.Count > 0 && index >= 0) Select(_tiles[Math.Min(index, _tiles.Count - 1)].Entry);
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Shell.Open(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Log.Warn($"The captures folder could not be opened ({e.GetType().Name})");
            Toast.Show(Strings.OpenFailed);
        }
    }
}
