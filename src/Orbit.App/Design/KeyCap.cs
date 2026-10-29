using System.Windows;
using System.Windows.Controls;

namespace Orbit.App.Design;

/// <summary>A key drawn as a small cap ("Ctrl", "K", "↵"), as Orbit's hints show shortcuts.</summary>
internal static class KeyCap
{
    public static FrameworkElement Make(string label, Palette palette) => new Border
    {
        MinWidth = 20, Height = 20, Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(4, 0, 0, 0), CornerRadius = new CornerRadius(5),
        Background = palette.Raised, VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = label, FontSize = 11, Foreground = palette.InkSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };
}
