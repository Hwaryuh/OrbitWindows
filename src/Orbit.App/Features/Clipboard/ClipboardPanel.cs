using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Orbit.App.Design;
using Orbit.App.Features.Capture;
using Orbit.App.Features.Capture.Editor;
using Orbit.App.Overlays;
using Orbit.Core.Clipboard;
using Orbit.Core.Display;

namespace Orbit.App.Features.Clipboard;

/// <summary>
/// The clipboard history as a shelf along the bottom of the monitor under the pointer (Orbit:
/// ClipboardPanel.swift, ClipboardView.swift). Type to search; ← → pick, ↑ ↓ change the filter,
/// Enter pastes (Shift: plain text, Ctrl: copy only), Ctrl+1–9 paste that card, Ctrl+P pins,
/// Ctrl+Delete deletes, Esc clears the search or closes. A click pastes. Closes when it loses focus.
/// </summary>
internal sealed class ClipboardPanel : OverlayWindow
{
    private const double PanelHeight = 300, Shadow = 12, CardSize = 220;

    private readonly ClipboardFeature _feature;
    private readonly AppServices _services;
    private readonly Palette _palette;
    private readonly TextBox _search;
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _cards = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 12, 16, 12) };
    private readonly ScrollViewer _scroll;
    private readonly Grid _body = new();
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyTitle, _emptyDetail;
    private readonly Button _paused;
    private ClipFilter _filter = ClipFilter.All;
    private IReadOnlyList<ClipItem> _results = [];
    private int _selection;
    private nint _target;

    public ClipboardPanel(ClipboardFeature feature, AppServices services) : base(activating: true, transparent: true, clickThrough: false)
    {
        _feature = feature;
        _services = services;
        _palette = Palette.Current();
        _search = _palette.PlainField(13);
        _search.Width = 220;
        _search.TextChanged += (_, _) =>
        {
            _selection = 0;
            Rebuild();
        };
        InputMethod.SetIsInputMethodEnabled(_search, true);

        _scroll = new ScrollViewer { Content = _cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        _emptyTitle = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        _emptyDetail = new TextBlock { FontSize = 12, Foreground = _palette.InkSecondary, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        _empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _empty.Children.Add(new TextBlock { Text = "", FontFamily = Glyphs.Font, FontSize = 28, Foreground = _palette.InkSecondary, HorizontalAlignment = HorizontalAlignment.Center });
        _empty.Children.Add(_emptyTitle);
        _empty.Children.Add(_emptyDetail);
        _body.Children.Add(_scroll);
        _body.Children.Add(_empty);

        _paused = new Button
        {
            Content = new TextBlock { Text = Strings.ClipPaused, FontSize = 12, FontWeight = FontWeights.Medium, Foreground = _palette.Accent },
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Template = Controls.RoundTemplate(0), Cursor = Cursors.Hand,
            ToolTip = Strings.ClipPausedHint, Focusable = false, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        _paused.Click += (_, _) =>
        {
            _feature.Resume();
            _paused.Visibility = Visibility.Collapsed;
        };

        var layout = new DockPanel();
        var toolbar = Toolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        var line = new Border { Height = 1, Background = _palette.Hairline };
        DockPanel.SetDock(line, Dock.Top);
        layout.Children.Add(line);
        layout.Children.Add(_body);
        var inner = new Border { Child = layout };
        inner.SizeChanged += (_, e) => inner.Clip = new RectangleGeometry(new Rect(e.NewSize), 19, 19);
        Content = new Border
        {
            Margin = new Thickness(Shadow), Background = _palette.Canvas, BorderBrush = _palette.Hairline, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20), Child = inner,
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Direction = 270, Opacity = 0.22 },
        };
        PreviewKeyDown += KeyPressed;
        Deactivated += (_, _) => Dismiss();
        _feature.History.Changed += () =>
        {
            if (IsVisible) Rebuild();
        };
    }

    /// <summary>Opens empty along the bottom of the pointer's monitor, keyboard in the search field.</summary>
    public void Present()
    {
        // Where the paste goes: the window that was in front, or the one before Orbit's.
        _target = _services.Foreground.LastExternal;
        _search.Text = "";
        _filter = ClipFilter.All;
        _selection = 0;
        _paused.Visibility = _feature.Paused ? Visibility.Visible : Visibility.Collapsed;
        Rebuild();
        var (x, y) = Monitors.Cursor();
        var monitor = Monitors.FromPoint(x, y)!;
        var work = monitor.WorkArea;
        double s = monitor.Scale;
        int side = (int)Math.Round((24 - Shadow) * s), bottom = (int)Math.Round((16 - Shadow) * s);
        int height = (int)Math.Round((PanelHeight + Shadow * 2) * s);
        ShowAt(new PixelRect(work.X + side, work.Bottom - bottom - height, work.Width - side * 2, height));
        FrontWindow.Take(Handle);
        Activate();
        _search.Focus();
        Keyboard.Focus(_search);
    }

    public void Dismiss()
    {
        if (IsVisible) Hide();
    }

    // MARK: Building

    private FrameworkElement Toolbar()
    {
        var bar = new DockPanel { Height = 44, Margin = new Thickness(16, 0, 16, 0) };
        var hints = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (keys, title) in new[] { ("Enter", Strings.ClipHintPaste), ("Shift Enter", Strings.ClipHintPlain), ("Ctrl P", Strings.ClipHintPin), ("↑ ↓", Strings.ClipHintFilter) })
        {
            var hint = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(hints.Children.Count == 0 ? 0 : 14, 0, 0, 0) };
            hint.Children.Add(new TextBlock { Text = title, FontSize = 11, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) });
            hint.Children.Add(KeyCap.Row(keys.Split(' '), _palette));
            hints.Children.Add(hint);
        }
        DockPanel.SetDock(hints, Dock.Right);
        bar.Children.Add(hints);
        DockPanel.SetDock(_paused, Dock.Right);
        bar.Children.Add(_paused);

        var search = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        search.Children.Add(new TextBlock { Text = Glyphs.Search, FontFamily = Glyphs.Font, FontSize = 13, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        search.Children.Add(_palette.WithHint(_search, Strings.ClipSearch, 2));
        DockPanel.SetDock(search, Dock.Left);
        bar.Children.Add(search);
        _chips.Margin = new Thickness(12, 0, 0, 0);
        bar.Children.Add(_chips);
        return bar;
    }

    private void Rebuild()
    {
        _results = _feature.History.Search(_search.Text, _filter);
        _selection = Math.Clamp(_selection, 0, Math.Max(0, _results.Count - 1));
        FillChips();
        _cards.Children.Clear();
        for (int i = 0; i < _results.Count; i++) _cards.Children.Add(Card(_results[i], i));
        bool none = _results.Count == 0;
        _scroll.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        _empty.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        bool searching = _search.Text.Trim().Length > 0;
        _emptyTitle.Text = searching ? Strings.ClipNoMatch(_search.Text.Trim()) : Strings.ClipEmpty;
        _emptyDetail.Text = searching ? Strings.ClipNoMatchDetail : Strings.ClipEmptyDetail;
        ScrollToSelection();
    }

    private void FillChips()
    {
        _chips.Children.Clear();
        foreach (var filter in ClipFilter.Cycle)
        {
            var title = filter.PinnedOnly ? Strings.ClipPinned : filter.Kind is { } kind ? Strings.ClipKindName(kind) : Strings.ClipAll;
            bool on = filter == _filter;
            var chip = new Button
            {
                Content = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = on ? _palette.Canvas : _palette.InkSecondary },
                Height = 24, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 0), Background = on ? _palette.Ink : _palette.Raised,
                BorderThickness = new Thickness(0), Template = Controls.RoundTemplate(12), Cursor = Cursors.Hand, Focusable = false,
            };
            var picked = filter;
            chip.Click += (_, _) =>
            {
                _filter = picked;
                _selection = 0;
                Rebuild();
            };
            _chips.Children.Add(chip);
        }
    }

    private FrameworkElement Card(ClipItem item, int index)
    {
        bool selected = index == _selection;
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

        var header = new DockPanel { Background = Palette.Fade(_palette.Raised, 0.6), LastChildFill = true };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        if (item.Pinned) right.Children.Add(new TextBlock { Text = "", FontFamily = Glyphs.Font, FontSize = 10, Foreground = _palette.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        if (index < 9) right.Children.Add(KeyCap.Make($"Ctrl {index + 1}", _palette));
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);
        var icon = new TextBlock { Text = KindGlyph(item.Kind), FontFamily = Glyphs.Font, FontSize = 14, Foreground = _palette.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = Strings.ClipKindName(item.Kind), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink });
        titles.Children.Add(new TextBlock { Text = item.AppName ?? Strings.ClipUnknownApp, FontSize = 11, Foreground = _palette.InkSecondary, TextTrimming = TextTrimming.CharacterEllipsis });
        header.Children.Add(titles);
        grid.Children.Add(header);

        var content = Body(item);
        Grid.SetRow(content, 1);
        grid.Children.Add(content);

        var footer = new DockPanel { Margin = new Thickness(10, 0, 10, 0) };
        var when = new TextBlock { Text = Strings.CapturedWhen(item.Date, DateTime.Now), FontSize = 11, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(when, Dock.Right);
        footer.Children.Add(when);
        footer.Children.Add(new TextBlock { Text = Footnote(item), FontSize = 11, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) });
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        var clip = new Border { Child = grid };
        clip.SizeChanged += (_, e) => clip.Clip = new RectangleGeometry(new Rect(e.NewSize), 11, 11);
        var card = new Border
        {
            Width = CardSize, Height = CardSize, Margin = new Thickness(0, 0, 12, 0), Background = _palette.Surface, CornerRadius = new CornerRadius(12),
            BorderBrush = selected ? _palette.Accent : _palette.Hairline, BorderThickness = new Thickness(selected ? 2 : 1), Child = clip, Cursor = Cursors.Hand,
        };
        Point? press = null;
        card.MouseLeftButtonDown += (_, e) => press = e.GetPosition(this);
        card.MouseMove += (_, e) =>
        {
            if (press is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            press = null;
            if (DragData(item) is { } data) DragDrop.DoDragDrop(card, data, DragDropEffects.Copy);
        };
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (press is null) return;
            press = null;
            _selection = index;
            Paste(item, plain: false);
        };
        card.ContextMenu = Menu(item);
        return card;
    }

    private FrameworkElement Body(ClipItem item)
    {
        switch (item.Kind)
        {
            case ClipKind.Link:
            {
                var panel = new DockPanel { Margin = new Thickness(10) };
                var globe = new TextBlock { Text = Glyphs.Globe, FontFamily = Glyphs.Font, FontSize = 22, Foreground = _palette.Ink };
                DockPanel.SetDock(globe, Dock.Top);
                panel.Children.Add(globe);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
                var host = Uri.TryCreate(item.Text, UriKind.Absolute, out var uri) ? uri.Host : "";
                text.Children.Add(new TextBlock { Text = host, FontSize = 13, FontWeight = FontWeights.Medium, Foreground = _palette.Ink, TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(new TextBlock { Text = item.Text, FontSize = 12, Foreground = _palette.InkSecondary, TextWrapping = TextWrapping.Wrap, MaxHeight = 48, TextTrimming = TextTrimming.CharacterEllipsis });
                panel.Children.Add(text);
                return panel;
            }
            case ClipKind.Image:
            {
                var image = new Image { Source = _feature.Thumbnail(item, 440), Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                return new Border { Background = _palette.Raised, ClipToBounds = true, Child = image };
            }
            case ClipKind.File:
            {
                var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10) };
                bool folder = item.FilePaths is [var only] && Directory.Exists(only);
                panel.Children.Add(new TextBlock { Text = folder ? Glyphs.Folder : Glyphs.Document, FontFamily = Glyphs.Font, FontSize = 40, Foreground = _palette.Ink, HorizontalAlignment = HorizontalAlignment.Center });
                panel.Children.Add(new TextBlock { Text = item.Title, FontSize = 13, Foreground = _palette.Ink, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxHeight = 36, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 8, 0, 0) });
                return panel;
            }
            case ClipKind.Color:
            {
                var grid = new Grid { Background = AnnotationRenderer.ToBrush(ClipItem.ColorValue(item.Text) ?? 0xFF808080) };
                grid.Children.Add(new TextBlock
                {
                    Text = item.Text?.Trim(), FontSize = 15, FontWeight = FontWeights.SemiBold, FontFamily = Glyphs.Mono, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10),
                    Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.6 },
                });
                return grid;
            }
            default:
                return new TextBlock
                {
                    Text = item.Text, Margin = new Thickness(10), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
                    FontFamily = item.LooksLikeCode ? Glyphs.Mono : FontFamily, FontSize = item.LooksLikeCode ? 11 : 13,
                    Foreground = Palette.Fade(_palette.Ink, 0.9),
                };
        }
    }

    private static string Footnote(ClipItem item) => item.Kind switch
    {
        ClipKind.Text => Strings.ClipCharacters((item.Text ?? "").Length, (item.Text ?? "").Split('\n').Length),
        ClipKind.Link => Uri.TryCreate(item.Text, UriKind.Absolute, out var uri) ? uri.Host : "",
        ClipKind.Image => $"{item.ImageWidth} × {item.ImageHeight} px",
        ClipKind.File => Strings.ClipFiles(item.FilePaths?.Count ?? 0),
        _ => item.Text?.Trim() ?? "",
    };

    private static string KindGlyph(ClipKind kind) => kind switch
    {
        ClipKind.Link => "",
        ClipKind.Image => "",
        ClipKind.File => Glyphs.Document,
        ClipKind.Color => "",
        _ => "",
    };

    private ContextMenu Menu(ClipItem item)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Controls.MenuItem(Strings.ClipPaste, () => Paste(item, plain: false)));
        menu.Items.Add(Controls.MenuItem(Strings.ClipPastePlain, () => Paste(item, plain: true)));
        menu.Items.Add(Controls.MenuItem(Strings.ActionCopy, () => CopyOnly(item)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Controls.MenuItem(item.Pinned ? Strings.ClipUnpin : Strings.ClipPin, () => _feature.History.TogglePin(item.Id)));
        if (item.Kind == ClipKind.Link && item.Text is { } link)
            menu.Items.Add(Controls.MenuItem(Strings.ClipOpenLink, () => Process.Start(new ProcessStartInfo(link.Trim()) { UseShellExecute = true })));
        if (item.Kind == ClipKind.File && item.FilePaths is [var first, ..])
            menu.Items.Add(Controls.MenuItem(Strings.CardShowInExplorer, () => Shell.Reveal(first)));
        if (item.Kind == ClipKind.Image && item.ImageFile is { } file && _services.CaptureCards is { } cards)
        {
            menu.Items.Add(Controls.MenuItem(Strings.CardEdit, () =>
            {
                if (CaptureItem.Load(_feature.History.PathOf(file)) is not { } image) return;
                Dismiss();
                EditorWindow.Open(image, cards, _services.Preferences);
            }));
            menu.Items.Add(Controls.MenuItem(Strings.CardPin, () =>
            {
                if (CaptureItem.Load(_feature.History.PathOf(file)) is not { } image) return;
                Dismiss();
                PinWindow.Pin(new CaptureItem("", image.Image), cards);
            }));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Controls.MenuItem(Strings.Delete, () => _feature.History.Delete(item.Id)));
        return menu;
    }

    private DataObject? DragData(ClipItem item)
    {
        var data = new DataObject();
        switch (item.Kind)
        {
            case ClipKind.Image when item.ImageFile is { } file:
                data.SetData(DataFormats.FileDrop, new[] { _feature.History.PathOf(file) });
                return data;
            case ClipKind.File when item.FilePaths is { Count: > 0 } files:
                data.SetData(DataFormats.FileDrop, files.ToArray());
                return data;
            case ClipKind.Text or ClipKind.Link or ClipKind.Color when item.Text is { } text:
                data.SetText(text);
                return data;
            default:
                return null;
        }
    }

    // MARK: Keys and actions

    private ClipItem? Selected => _selection < _results.Count ? _results[_selection] : null;

    private void Move(int delta)
    {
        if (_results.Count == 0) return;
        _selection = Math.Clamp(_selection + delta, 0, _results.Count - 1);
        for (int i = 0; i < _cards.Children.Count; i++)
            if (_cards.Children[i] is Border card)
            {
                card.BorderBrush = i == _selection ? _palette.Accent : _palette.Hairline;
                card.BorderThickness = new Thickness(i == _selection ? 2 : 1);
            }
        ScrollToSelection();
    }

    private void ScrollToSelection()
    {
        if (_selection < _cards.Children.Count && _cards.Children[_selection] is FrameworkElement card)
            Dispatcher.BeginInvoke(() => card.BringIntoView());
    }

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0, shift = (mods & ModifierKeys.Shift) != 0;
        // A word being composed belongs to the IME.
        if (key == Key.ImeProcessed) return;
        switch (key)
        {
            case Key.Left:
                Move(-1);
                break;
            case Key.Right:
                Move(1);
                break;
            case Key.Tab:
                Move(shift ? -1 : 1);
                break;
            case Key.Down or Key.Up:
                _filter = _filter.Next(backward: key == Key.Up);
                _selection = 0;
                Rebuild();
                break;
            case Key.Escape:
                if (_search.Text.Length > 0) _search.Text = "";
                else Dismiss();
                break;
            case Key.Enter when Selected is { } item:
                if (ctrl) CopyOnly(item, shift);
                else Paste(item, shift);
                break;
            case >= Key.D1 and <= Key.D9 when ctrl:
                int n = key - Key.D1;
                if (n < _results.Count) Paste(_results[n], plain: false);
                break;
            // Ctrl+C copies the search field's own selection when there is one.
            case Key.C when ctrl && _search.SelectionLength == 0 && Selected is { } item:
                CopyOnly(item);
                break;
            case Key.P when ctrl && Selected is { } item:
                _feature.History.TogglePin(item.Id);
                break;
            case Key.Delete when ctrl && Selected is { } item:
                _feature.History.Delete(item.Id);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void CopyOnly(ClipItem item, bool plain = false)
    {
        bool copied = _feature.Copy(item, plain);
        Dismiss();
        Toast.Show(copied ? Strings.Copied : Strings.CopyFailed);
    }

    /// <summary>Straight out, no fade: the panel lets go of the keyboard before Ctrl+V is sent.</summary>
    private void Paste(ClipItem item, bool plain)
    {
        var target = _target;
        Dismiss();
        _ = _feature.PasteAsync(item, plain, target);
    }
}
