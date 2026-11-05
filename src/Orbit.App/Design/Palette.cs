using System.Windows.Media;
using Microsoft.Win32;

namespace Orbit.App.Design;

/// <summary>
/// Orbit's monochrome palette (Design/Theme.swift): every color has a light and a dark value; the
/// only hue is the orange accent. Light or dark follows Windows' app mode, read when a surface opens.
/// </summary>
internal sealed class Palette
{
    private Palette(bool dark)
    {
        Dark = dark;
        Canvas = Solid(dark ? 0x0B0B0C : 0xFFFFFF);
        Sidebar = Solid(dark ? 0x070708 : 0xF6F6F7);
        Surface = Solid(dark ? 0x18181A : 0xF4F4F5);
        Raised = Solid(dark ? 0x242427 : 0xE9E9EB);
        Ink = Solid(dark ? 0xFAFAFA : 0x0B0B0C);
        InkSecondary = Solid(dark ? 0x9C9CA3 : 0x6B6B70);
        Hairline = Solid(dark ? 0xFFFFFF : 0x000000, dark ? 0.11 : 0.09);
        Accent = Solid(dark ? 0xFF7338 : 0xF55C1A);
        Selection = Fade(Ink, 0.08);
    }

    public bool Dark { get; }
    /// <summary>Window and panel background.</summary>
    public SolidColorBrush Canvas { get; }
    /// <summary>The settings window's left side.</summary>
    public SolidColorBrush Sidebar { get; }
    /// <summary>Cards, rows, grouped content.</summary>
    public SolidColorBrush Surface { get; }
    /// <summary>Controls resting on a surface.</summary>
    public SolidColorBrush Raised { get; }
    public SolidColorBrush Ink { get; }
    public SolidColorBrush InkSecondary { get; }
    public SolidColorBrush Hairline { get; }
    /// <summary>Orbit orange.</summary>
    public SolidColorBrush Accent { get; }
    /// <summary>A selected row's background.</summary>
    public SolidColorBrush Selection { get; }

    /// <summary>A borderless text field on this palette, as the launcher's search fields.</summary>
    public System.Windows.Controls.TextBox PlainField(double fontSize) => new()
    {
        FontSize = fontSize, BorderThickness = new System.Windows.Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
        Foreground = Ink, CaretBrush = Ink, VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
    };

    /// <summary>
    /// <paramref name="field"/> with a hint shown in it while it is empty. The hint sits
    /// <paramref name="hintIndent"/> in from the field's own left edge, where its text starts.
    /// </summary>
    public System.Windows.Controls.Grid WithHint(System.Windows.Controls.TextBox field, string hint, double hintIndent)
    {
        var text = new System.Windows.Controls.TextBlock
        {
            Text = hint, FontSize = field.FontSize, Foreground = InkSecondary, IsHitTestVisible = false,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new System.Windows.Thickness(field.Margin.Left + hintIndent, 0, 0, 0),
        };
        field.TextChanged += (_, _) => text.Visibility = field.Text.Length == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        var grid = new System.Windows.Controls.Grid();
        grid.Children.Add(text);
        grid.Children.Add(field);
        return grid;
    }

    public static Palette Current() => new(!AppsUseLightTheme());

    /// <summary>The light palette whatever Windows uses: the settings window is always white.</summary>
    public static Palette Light { get; } = new(false);

    /// <summary>The dark palette whatever Windows uses: labels over the capture overlay's dimmed screen.</summary>
    public static Palette DarkTheme { get; } = new(true);

    /// <summary><paramref name="brush"/>'s color at <paramref name="opacity"/>.</summary>
    public static SolidColorBrush Fade(SolidColorBrush brush, double opacity)
    {
        var faded = new SolidColorBrush(brush.Color) { Opacity = brush.Opacity * opacity };
        faded.Freeze();
        return faded;
    }

    private static bool AppsUseLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    private static SolidColorBrush Solid(int rgb, double opacity = 1)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }
}
