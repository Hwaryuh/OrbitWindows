using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Orbit.App.Design;
using Orbit.App.Overlays;
using Orbit.Core.Display;
using Orbit.Core.Snippets;
using Windows.Win32.Foundation;

namespace Orbit.App.Features.Snippets;

/// <summary>
/// Search every snippet and drop one in where you're typing (Orbit: <c>SnippetPanel</c>): for
/// the ones without a keyword, or a forgotten keyword. ↑ ↓ pick, Enter inserts, Ctrl+Enter copies,
/// Esc clears the search or closes. Closes when it loses focus.
/// </summary>
internal sealed class SnippetPanel : OverlayWindow
{
    private const double PanelWidth = 640, PanelHeight = 400, Shadow = 12;

    private readonly SnippetFeature _feature;
    private readonly AppServices _services;
    private readonly Palette _palette;
    private readonly TextBox _search;
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _listScroll;
    private readonly TextBlock _preview;
    private readonly Grid _body = new();
    private readonly StackPanel _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private IReadOnlyList<Snippet> _results = [];
    private int _selection;
    private nint _target;

    public SnippetPanel(SnippetFeature feature, AppServices services) : base(activating: true, transparent: true, clickThrough: false)
    {
        _feature = feature;
        _services = services;
        _palette = Palette.Current();
        _search = _palette.PlainField(17);
        _search.TextChanged += (_, _) =>
        {
            _selection = 0;
            Rebuild();
        };
        _listScroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Width = 250, Padding = new Thickness(6), Focusable = false };
        _preview = new TextBlock { FontSize = 13, Foreground = _palette.Ink, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16) };
        var split = new DockPanel();
        DockPanel.SetDock(_listScroll, Dock.Left);
        split.Children.Add(_listScroll);
        split.Children.Add(new Border { BorderBrush = _palette.Hairline, BorderThickness = new Thickness(1, 0, 0, 0), Child = new ScrollViewer { Content = _preview, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false } });
        _empty.Children.Add(new TextBlock { Text = Strings.SnippetNone, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink, HorizontalAlignment = HorizontalAlignment.Center });
        _empty.Children.Add(new TextBlock { Text = Strings.SnippetNoneDetail, FontSize = 12, Foreground = _palette.InkSecondary, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
        _body.Children.Add(split);
        _body.Children.Add(_empty);

        var layout = new DockPanel();
        var head = new DockPanel { Height = 52, Margin = new Thickness(16, 0, 16, 0) };
        var icon = new TextBlock { Text = "", FontFamily = Glyphs.Font, FontSize = 16, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(icon, Dock.Left);
        head.Children.Add(icon);
        head.Children.Add(_palette.WithHint(_search, Strings.SnippetSearch, 2));
        DockPanel.SetDock(head, Dock.Top);
        layout.Children.Add(head);
        var line = new Border { Height = 1, Background = _palette.Hairline };
        DockPanel.SetDock(line, Dock.Top);
        layout.Children.Add(line);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 16, 0), Height = 32 };
        foreach (var (keys, title) in new[] { ("Enter", Strings.SnippetInsert), ("Ctrl Enter", Strings.ActionCopy) })
        {
            var hint = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            hint.Children.Add(new TextBlock { Text = title, FontSize = 11, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2, 0) });
            hint.Children.Add(KeyCap.Row(keys.Split(' '), _palette));
            footer.Children.Add(hint);
        }
        var footerFrame = new Border { BorderBrush = _palette.Hairline, BorderThickness = new Thickness(0, 1, 0, 0), Child = footer };
        DockPanel.SetDock(footerFrame, Dock.Bottom);
        layout.Children.Add(footerFrame);
        layout.Children.Add(_body);
        var inner = new Border { Child = layout };
        inner.SizeChanged += (_, e) => inner.Clip = new RectangleGeometry(new Rect(e.NewSize), 19, 19);
        Content = new Border
        {
            Margin = new Thickness(Shadow), Background = _palette.Canvas, BorderBrush = _palette.Hairline, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20), Child = inner,
            Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 10, Direction = 270, Opacity = 0.22 },
        };
        PreviewKeyDown += KeyPressed;
        Deactivated += (_, _) => Dismiss();
    }

    public void Present()
    {
        _target = _services.Foreground.LastExternal;
        _search.Text = "";
        _selection = 0;
        Rebuild();
        var (x, y) = Monitors.Cursor();
        var monitor = Monitors.FromPoint(x, y)!;
        var work = monitor.WorkArea;
        double s = monitor.Scale;
        int w = (int)Math.Round((PanelWidth + Shadow * 2) * s), h = (int)Math.Round((PanelHeight + Shadow * 2) * s);
        ShowAt(new PixelRect(work.X + (work.Width - w) / 2, Math.Max(work.Y, work.Y + (int)(work.Height * 0.42) - h / 2), w, h));
        FrontWindow.Take(Handle);
        Activate();
        _search.Focus();
        Keyboard.Focus(_search);
    }

    public void Dismiss()
    {
        if (IsVisible) Hide();
    }

    private void Rebuild()
    {
        _results = _feature.Store.Search(_search.Text);
        _selection = Math.Clamp(_selection, 0, Math.Max(0, _results.Count - 1));
        _list.Children.Clear();
        for (int i = 0; i < _results.Count; i++) _list.Children.Add(Row(_results[i], i));
        _empty.Visibility = _results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ((FrameworkElement)_body.Children[0]).Visibility = _results.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _preview.Text = _results.Count > 0 ? _results[_selection].Content : "";
        if (_selection < _list.Children.Count) ((FrameworkElement)_list.Children[_selection]).BringIntoView();
    }

    private FrameworkElement Row(Snippet snippet, int index)
    {
        bool on = index == _selection;
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        if (snippet.Keyword.Length > 0)
        {
            var keyword = new Border
            {
                Background = on ? Palette.Fade(_palette.Canvas, 0.18) : _palette.Raised, CornerRadius = new CornerRadius(10), Padding = new Thickness(6, 1, 6, 1), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = snippet.Keyword, FontSize = 11, FontWeight = FontWeights.SemiBold, FontFamily = Glyphs.Mono, Foreground = on ? _palette.Canvas : _palette.Ink },
            };
            DockPanel.SetDock(keyword, Dock.Right);
            row.Children.Add(keyword);
        }
        var text = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        text.Children.Add(new TextBlock { Text = snippet.Name.Length > 0 ? snippet.Name : Strings.SnippetUntitled, FontSize = 13, FontWeight = FontWeights.Medium, Foreground = on ? _palette.Canvas : _palette.Ink, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = snippet.Content.ReplaceLineEndings(" "), FontSize = 12, Foreground = on ? Palette.Fade(_palette.Canvas, 0.7) : _palette.InkSecondary, TextTrimming = TextTrimming.CharacterEllipsis });
        row.Children.Add(text);
        var frame = new Border { Background = on ? _palette.Ink : Brushes.Transparent, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 7), Child = row, Cursor = Cursors.Hand };
        frame.MouseLeftButtonUp += (_, _) =>
        {
            _selection = index;
            Insert(snippet);
        };
        return frame;
    }

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.ImeProcessed) return;
        var mods = Keyboard.Modifiers;
        var selected = _selection < _results.Count ? _results[_selection] : null;
        switch (e.Key)
        {
            case Key.Up:
                Move(-1);
                break;
            case Key.Down:
                Move(1);
                break;
            case Key.Tab:
                Move((mods & ModifierKeys.Shift) != 0 ? -1 : 1);
                break;
            case Key.Escape:
                if (_search.Text.Length > 0) _search.Text = "";
                else Dismiss();
                break;
            case Key.Enter when selected is not null:
                if ((mods & ModifierKeys.Control) != 0)
                {
                    Dismiss();
                    _feature.CopyRendered(selected);
                }
                else Insert(selected);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Move(int delta)
    {
        if (_results.Count == 0) return;
        _selection = Math.Clamp(_selection + delta, 0, _results.Count - 1);
        Rebuild();
    }

    /// <summary>Straight out, then into the window that was in front when the panel opened.</summary>
    private void Insert(Snippet snippet)
    {
        Dismiss();
        var target = new HWND(_target);
        _ = InsertAsync(snippet, target);
    }

    private async Task InsertAsync(Snippet snippet, HWND target)
    {
        if (target.IsNull) return;
        await _feature.InsertAsync(snippet, target);
    }
}
