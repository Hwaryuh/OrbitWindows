using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Orbit.App.Design;

/// <summary>Small pieces of WPF control setup that several surfaces share.</summary>
internal static class Controls
{
    /// <summary>A button drawn as its background with rounded corners and its content centered.</summary>
    public static ControlTemplate RoundTemplate(double radius)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new Binding(nameof(Control.Padding)) { RelativeSource = RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    /// <summary>The brush, pen or geometry, frozen, for use from any drawing.</summary>
    public static T Frozen<T>(this T freezable) where T : System.Windows.Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    public static MenuItem MenuItem(string header, Action click)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => click();
        return item;
    }
}
