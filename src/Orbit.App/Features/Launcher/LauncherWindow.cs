using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Orbit.App.Design;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Display;
using Orbit.Core.Launcher;

namespace Orbit.App.Features.Launcher;

/// <summary>
/// The command palette (Orbit: LauncherPanel.swift, LauncherView.swift): a search field, results
/// in sections, a footer naming the primary action, and Ctrl+K for every action of the selected
/// result (<see cref="ActionPanel"/>). Above the middle of the monitor under the pointer; gone when it loses the keyboard.
/// Keys: ↑↓, Enter, Ctrl+Enter (second action), Ctrl+K, Esc (actions, then the query, then the
/// window), Ctrl+, (settings).
/// </summary>
internal sealed class LauncherWindow : OverlayWindow
{
    private const double PanelWidth = 680, PanelHeight = 480, ShadowMargin = 24;
    private const double SearchHeight = 56, FooterHeight = 36, RowHeight = 40;
    /// <summary>The panel's corner, and the same corner inside its 1-DIP border.</summary>
    private const double CornerRadiusOutside = 16, CornerRadiusInside = 15;
    private static readonly TimeSpan RunDelay = TimeSpan.FromMilliseconds(50);

    private readonly LauncherModel _model;
    private readonly AppIndex _apps;
    private readonly Palette _palette;
    private readonly TextBox _query;
    private readonly StackPanel _rows = new();
    private readonly ScrollViewer _scroll;
    private readonly FrameworkElement _empty;
    private readonly TextBlock _primaryHint;
    private readonly ActionPanel _actionPanel;
    /// <summary>Icons by app or path, loading or loaded; a row built while one loads waits for the same task.</summary>
    private readonly Dictionary<string, Task<ImageSource?>> _icons = [];
    private IReadOnlyList<OpenWindow> _windows = [];
    private IReadOnlyList<LauncherItem> _items = [];
    private int _selection;
    private (int X, int Y) _openedAt;

    public LauncherWindow(LauncherModel model, AppIndex apps, Palette palette) : base(activating: true, transparent: true, clickThrough: false)
    {
        _model = model;
        _apps = apps;
        _palette = palette;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Malgun Gothic");

        _query = palette.PlainField(18);
        _query.Margin = new Thickness(12, 0, 0, 0);
        var placeholder = new TextBlock
        {
            Text = Strings.LauncherPlaceholder, FontSize = 18, Foreground = palette.InkSecondary, IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 0, 0, 0),
        };
        _query.TextChanged += (_, _) =>
        {
            placeholder.Visibility = _query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Rebuild();
        };
        var searchField = new Grid();
        searchField.Children.Add(placeholder);
        searchField.Children.Add(_query);
        var search = new DockPanel { Height = SearchHeight, Margin = new Thickness(18, 0, 18, 0) };
        var mark = new OrbitMark { Size = 22, Ink = palette.Ink, Accent = palette.Accent, Dot = true, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(mark, Dock.Left);
        search.Children.Add(mark);
        search.Children.Add(searchField);

        _scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Padding = new Thickness(8, 0, 8, 8) };
        _empty = EmptyState(palette);

        _primaryHint = new TextBlock { FontSize = 12, Foreground = palette.Ink, VerticalAlignment = VerticalAlignment.Center };
        var footer = new DockPanel { Height = FooterHeight, Background = Palette.Fade(palette.Surface, 0.6) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new OrbitMark { Size = 15, Ink = palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(new TextBlock { Text = "Orbit", FontSize = 12, FontWeight = FontWeights.Medium, Foreground = palette.InkSecondary, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(brand, Dock.Left);
        footer.Children.Add(brand);
        var hints = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        hints.Children.Add(_primaryHint);
        hints.Children.Add(KeyCap.Make("↵", palette));
        hints.Children.Add(new Border { Width = 1, Height = 14, Background = palette.Hairline, Margin = new Thickness(12, 0, 12, 0) });
        hints.Children.Add(new TextBlock { Text = Strings.LauncherActions, FontSize = 12, Foreground = palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center });
        hints.Children.Add(KeyCap.Make("Ctrl", palette));
        hints.Children.Add(KeyCap.Make("K", palette));
        footer.Children.Add(hints);

        _actionPanel = new ActionPanel(palette, FooterHeight + 6);
        _actionPanel.Chosen += Run;

        var body = new DockPanel();
        var top = new Border { BorderBrush = Palette.Fade(palette.Hairline, 0.6), BorderThickness = new Thickness(0, 0, 0, 1), Child = search };
        var bottom = new Border { BorderBrush = Palette.Fade(palette.Hairline, 0.6), BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        body.Children.Add(top);
        body.Children.Add(bottom);
        var middle = new Grid();
        middle.Children.Add(_scroll);
        middle.Children.Add(_empty);
        body.Children.Add(middle);

        var layers = new Grid();
        layers.Children.Add(body);
        layers.Children.Add(_actionPanel);
        // ClipToBounds cuts square: the footer's fill showed past the rounded corners. Clip to the rounded shape instead.
        var inner = new Border { Child = layers };
        inner.SizeChanged += (_, e) => inner.Clip = new RectangleGeometry(new Rect(e.NewSize), CornerRadiusInside, CornerRadiusInside);
        Content = new Border
        {
            Margin = new Thickness(ShadowMargin), Background = palette.Canvas, BorderBrush = palette.Hairline, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CornerRadiusOutside), Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 12, Direction = 270, Opacity = 0.25 },
            Child = inner,
        };

        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => Dismiss();
        apps.Changed += () => Dispatcher.BeginInvoke(() => { if (IsVisible) Rebuild(); });
    }

    public Palette Palette => _palette;

    /// <summary>Opens fresh (an empty query) above the middle of the monitor under the pointer, with the keyboard.</summary>
    public void Present()
    {
        _windows = OpenWindows.List();
        _apps.RefreshIfOlderThan(TimeSpan.FromSeconds(60));
        CloseActions();
        _query.Text = "";
        Rebuild();

        var (x, y) = _openedAt = Monitors.Cursor();
        var monitor = Monitors.FromPoint(x, y)!;
        var work = monitor.WorkArea;
        int width = (int)Math.Round((PanelWidth + ShadowMargin * 2) * monitor.Scale);
        int height = (int)Math.Round((PanelHeight + ShadowMargin * 2) * monitor.Scale);
        // Optical center: a little above the middle, as Orbit and Spotlight sit.
        int left = work.X + (work.Width - width) / 2;
        int top = Math.Max(work.Y, work.Y + (int)(work.Height * 0.38) - height / 2);
        ShowAt(new PixelRect(left, top, width, height));
        FrontWindow.Take(Handle);
        Activate();
        _query.Focus();
        Keyboard.Focus(_query);
    }

    public void Dismiss()
    {
        if (!IsVisible) return;
        CloseActions();
        Hide();
    }

    private void Rebuild()
    {
        // The list changes under an open action panel: its result may be gone.
        if (_actionPanel.IsOpen) CloseActions();
        _items = _model.Build(_query.Text, _windows);
        _selection = 0;
        _rows.Children.Clear();
        string? section = null;
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (item.Section != section)
            {
                section = item.Section;
                _rows.Children.Add(new TextBlock
                {
                    Text = section, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = _palette.InkSecondary,
                    Margin = new Thickness(10, i == 0 ? 8 : 12, 0, 4),
                });
            }
            _rows.Children.Add(Row(item, i));
        }
        _empty.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _scroll.ScrollToTop();
        ShowSelection();
    }

    private FrameworkElement Row(LauncherItem item, int index)
    {
        var title = new TextBlock { Text = item.Title, FontSize = 14, Foreground = _palette.Ink, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var text = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        text.Children.Add(title);
        if (item.Subtitle is { } subtitle)
            text.Children.Add(new TextBlock { Text = subtitle, FontSize = 14, Foreground = _palette.InkSecondary, Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });

        var row = new DockPanel { Height = RowHeight, LastChildFill = true };
        var icon = IconFor(item);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        if (item.Accessory is { } accessory)
        {
            var label = new TextBlock { Text = accessory, FontSize = 12, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(label, Dock.Right);
            row.Children.Add(label);
        }
        row.Children.Add(text);

        var bar = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5), Background = _palette.Accent, HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed };
        var cell = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Tag = bar };
        cell.Children.Add(new Border { Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(8), Child = row });
        cell.Children.Add(bar);
        cell.MouseMove += (_, _) =>
        {
            // The launcher may open under a resting pointer: only a real move picks a row.
            var at = Monitors.Cursor();
            if (at == _openedAt || _selection == index || _actionPanel.IsOpen) return;
            _selection = index;
            ShowSelection();
        };
        cell.MouseLeftButtonUp += (_, _) => { _selection = index; RunPrimary(); };
        return cell;
    }

    private FrameworkElement IconFor(LauncherItem item)
    {
        switch (item.IconKind)
        {
            case IconKind.Mark:
                return new OrbitMark { Size = 20, Ink = _palette.Ink, Accent = _palette.Accent, Width = 24, VerticalAlignment = VerticalAlignment.Center };
            case IconKind.App or IconKind.File:
                var image = new Image { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                ShowIcon(item.Icon, image);
                return image;
            default:
                return new Border
                {
                    Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Background = _palette.Surface, VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = item.Icon, FontFamily = Glyphs.Font, FontSize = 13, Foreground = _palette.Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
        }
    }

    private async void ShowIcon(string key, Image image)
    {
        if (!_icons.TryGetValue(key, out var load)) _icons[key] = load = LoadIcon(key);
        // A failed icon is tried again the next time it shows, not remembered as missing.
        _ = load.ContinueWith(t => { if (!t.IsCompletedSuccessfully || t.Result is null) _icons.Remove(key); },
            TaskScheduler.FromCurrentSynchronizationContext());
        if (load.IsCompletedSuccessfully)
        {
            image.Source = load.Result;
            return;
        }
        try
        {
            image.Source = await load;
        }
        catch (Exception e)
        {
            Log.Error("Launcher icon failed", e);
        }
    }

    private async Task<ImageSource?> LoadIcon(string key)
    {
        var pixels = await _apps.IconAsync(key, 32);
        if (pixels is null) return null;
        var bitmap = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Pbgra32, null, pixels.Pixels, pixels.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private void ShowSelection()
    {
        int index = 0;
        foreach (var child in _rows.Children)
        {
            if (child is not Grid { Tag: Border bar } cell) continue;
            bool selected = index++ == _selection;
            ((Border)cell.Children[0]).Background = selected ? _palette.Selection : Brushes.Transparent;
            bar.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            if (selected) cell.BringIntoView();
        }
        _primaryHint.Text = Selected?.Actions.FirstOrDefault()?.Title ?? "";
        _primaryHint.Margin = new Thickness(0, 0, 6, 0);
    }

    private LauncherItem? Selected => _selection >= 0 && _selection < _items.Count ? _items[_selection] : null;

    private void OnKey(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        switch (e.Key)
        {
            case Key.Down: Move(1); break;
            case Key.Up: Move(-1); break;
            case Key.Enter when ctrl && !_actionPanel.IsOpen: RunSecondary(); break;
            case Key.Enter: if (_actionPanel.IsOpen) _actionPanel.RunSelected(); else RunPrimary(); break;
            case Key.K when ctrl: if (_actionPanel.IsOpen) CloseActions(); else OpenActions(); break;
            case Key.OemComma when ctrl:
                Dismiss();
                _model.OpenSettings();
                break;
            case Key.Escape:
                if (_actionPanel.IsOpen) CloseActions();
                else if (_query.Text.Length > 0) _query.Text = "";
                else Dismiss();
                break;
            default: return;
        }
        e.Handled = true;
    }

    private void Move(int delta)
    {
        if (_actionPanel.IsOpen) _actionPanel.Move(delta);
        else if (_items.Count > 0)
        {
            _selection = Math.Clamp(_selection + delta, 0, _items.Count - 1);
            ShowSelection();
        }
    }

    private void RunPrimary()
    {
        if (Selected is { } item && item.Actions.Count > 0) Run(item, item.Actions[0]);
    }

    private void RunSecondary()
    {
        if (Selected is { } item && item.Actions.Count > 1) Run(item, item.Actions[1]);
    }

    private void Run(LauncherItem item, LauncherAction action)
    {
        _model.Bump(item);
        if (action.BeforeHide)
        {
            action.Perform();
            Dismiss();
            return;
        }
        Dismiss();
        Later.Run(RunDelay, action.Perform);
    }

    private void OpenActions()
    {
        if (Selected is { } item) _actionPanel.Open(item);
    }

    private void CloseActions()
    {
        _actionPanel.Close();
        _query.Focus();
    }

    private static FrameworkElement EmptyState(Palette palette)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        panel.Children.Add(new TextBlock { Text = Glyphs.Search, FontFamily = Glyphs.Font, FontSize = 26, Foreground = palette.InkSecondary, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = Strings.LauncherEmpty, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = palette.Ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 2) });
        panel.Children.Add(new TextBlock { Text = Strings.LauncherEmptyHint, FontSize = 12, Foreground = palette.InkSecondary, HorizontalAlignment = HorizontalAlignment.Center });
        return panel;
    }
}
