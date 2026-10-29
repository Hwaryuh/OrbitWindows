using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Orbit.App.Design;

namespace Orbit.App.Features.Launcher;

/// <summary>
/// The panel Ctrl+K opens over the launcher (Orbit: LauncherView.swift, <c>ActionPanel</c>): every
/// action of one result, with a field to narrow them. It belongs to the result it was opened for,
/// whatever the launcher selects meanwhile.
/// </summary>
internal sealed class ActionPanel : Border
{
    private readonly Palette _palette;
    private readonly StackPanel _rows = new();
    private readonly TextBox _query;
    private List<LauncherAction> _actions = [];
    private int _selection;

    public ActionPanel(Palette palette, double bottomMargin)
    {
        _palette = palette;
        _query = palette.PlainField(13);
        _query.Height = 36;
        _query.Padding = new Thickness(10, 0, 10, 0);
        _query.TextChanged += (_, _) => Rebuild();
        var layout = new DockPanel();
        var search = new Border { BorderBrush = palette.Hairline, BorderThickness = new Thickness(0, 1, 0, 0), Child = _query };
        DockPanel.SetDock(search, Dock.Bottom);
        layout.Children.Add(search);
        layout.Children.Add(new Border { Padding = new Thickness(6), Child = _rows });

        Width = 290;
        Background = palette.Canvas;
        BorderBrush = palette.Hairline;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 10, bottomMargin);
        Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.22 };
        Visibility = Visibility.Collapsed;
        Child = layout;
    }

    /// <summary>The result the panel is open for; null while closed.</summary>
    public LauncherItem? Target { get; private set; }

    public bool IsOpen => Target is not null;

    /// <summary>An action was picked, with Enter or a click.</summary>
    public event Action<LauncherItem, LauncherAction>? Chosen;

    public void Open(LauncherItem target)
    {
        Target = target;
        _query.Text = "";
        Rebuild();
        Visibility = Visibility.Visible;
        _query.Focus();
    }

    public void Close()
    {
        Target = null;
        Visibility = Visibility.Collapsed;
    }

    public void Move(int delta)
    {
        if (_actions.Count == 0) return;
        _selection = (_selection + delta + _actions.Count) % _actions.Count;
        ShowSelection();
    }

    public void RunSelected()
    {
        if (Target is { } target && _selection < _actions.Count) Chosen?.Invoke(target, _actions[_selection]);
    }

    private void Rebuild()
    {
        var all = Target?.Actions ?? [];
        var q = _query.Text.Trim();
        _actions = q.Length == 0 ? [.. all] : all.Where(a => a.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        _selection = 0;
        _rows.Children.Clear();
        for (int i = 0; i < _actions.Count; i++)
        {
            var action = _actions[i];
            int index = i;
            var row = new DockPanel { Height = 32, Margin = new Thickness(6, 0, 6, 0) };
            var glyph = new TextBlock { Text = action.Glyph, FontFamily = Glyphs.Font, FontSize = 13, Foreground = _palette.InkSecondary, Width = 22, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(glyph, Dock.Left);
            row.Children.Add(glyph);
            if (i == 0)
            {
                var key = KeyCap.Make("↵", _palette);
                DockPanel.SetDock(key, Dock.Right);
                row.Children.Add(key);
            }
            row.Children.Add(new TextBlock { Text = action.Title, FontSize = 13, Foreground = _palette.Ink, VerticalAlignment = VerticalAlignment.Center });
            var cell = new Border { CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Child = row, Cursor = Cursors.Hand };
            cell.MouseLeftButtonUp += (_, _) => { _selection = index; RunSelected(); };
            _rows.Children.Add(cell);
        }
        ShowSelection();
    }

    private void ShowSelection()
    {
        for (int i = 0; i < _rows.Children.Count; i++)
            ((Border)_rows.Children[i]).Background = i == _selection ? _palette.Selection : Brushes.Transparent;
    }
}
