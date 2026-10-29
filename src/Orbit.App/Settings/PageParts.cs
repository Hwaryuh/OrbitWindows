using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Orbit.App.Design;

namespace Orbit.App.Settings;

/// <summary>The settings pages' shared pieces in Orbit's style: cards, titles, captions, pills, toggles.</summary>
internal sealed class PageParts(Palette palette)
{
    private readonly Palette _palette = palette;

    public Border Card(UIElement child, double padding = 16) => new()
    {
        Background = _palette.Surface, BorderBrush = _palette.Hairline, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
        Padding = new Thickness(padding), Child = child,
    };

    public UIElement CardTitle(string glyph, string title, string detail)
    {
        var icon = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(9), Background = _palette.Ink, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = glyph, FontFamily = Glyphs.Font, FontSize = 15, Foreground = _palette.Canvas, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = _palette.Ink });
        text.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = _palette.InkSecondary, TextWrapping = TextWrapping.Wrap });
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(text);
        return row;
    }

    public TextBlock SectionTitle(string text, double bottom = 0) => new()
    {
        Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = _palette.InkSecondary,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, bottom),
    };

    public TextBlock Caption(string text) =>
        new() { Text = text, FontSize = 11.5, Foreground = _palette.InkSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    public Button PillButton(string label, bool prominent, Action click)
    {
        var button = new Button
        {
            Content = label, Height = 28, Padding = new Thickness(12, 0, 12, 0), FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = prominent ? _palette.Canvas : _palette.Ink, Background = prominent ? _palette.Ink : _palette.Raised,
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Template = Controls.RoundTemplate(14),
        };
        button.Click += (_, _) => click();
        return button;
    }

    public CheckBox Toggle(bool on, Action<bool> changed)
    {
        var box = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 2, 0, 0), Cursor = Cursors.Hand };
        box.Click += (_, _) => changed(box.IsChecked == true);
        return box;
    }
}
