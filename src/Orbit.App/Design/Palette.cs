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

    public static Palette Current() => new(!AppsUseLightTheme());

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
