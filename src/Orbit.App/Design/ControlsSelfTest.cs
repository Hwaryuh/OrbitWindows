using static Orbit.App.SelfTestParts;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Orbit.App.Design;

/// <summary>
/// <c>--selftest-controls [--render file.png]</c>: every control in Design/ControlStyles.xaml, in each
/// palette, in a window outside every monitor. Drop-downs and menus aren't opened, as a popup would land
/// on a real monitor: menu items are drawn in a plain menu on a card, and the drop-down's parts are
/// checked. Open ones are checked by eye in the real windows.
/// </summary>
internal static class ControlsSelfTest
{
    public static bool Run(string? render)
    {
        var results = new SelfTestLog("Controls");
        foreach (var (name, palette) in new[] { ("light", Palette.Light), ("dark", Palette.DarkTheme) })
        {
            var (window, parts) = Gallery(palette);
            try
            {
                window.Show();
                Pump();
                results.Check($"{name}: the switch's knob is on when checked, at once", Knob(parts.SwitchOn) > 15);
                results.Check($"{name}: the switch's knob is off when not", Knob(parts.SwitchOff) < 3);
                parts.SwitchOff.IsChecked = true;
                results.Check($"{name}: a switch turned on starts from where it was", Knob(parts.SwitchOff) < 15);
                results.Check($"{name}: and arrives", Wait(() => Knob(parts.SwitchOff) > 15));
                parts.SwitchOff.IsChecked = false;
                results.Check($"{name}: and back", Wait(() => Knob(parts.SwitchOff) < 3));
                var fresh = Controls.MakeSwitch(new CheckBox());
                fresh.IsChecked = true;
                fresh.IsChecked = false;
                results.Check($"{name}: changing a switch not yet shown throws nothing", fresh.IsChecked == false);

                results.Check($"{name}: the combo box has its drop-down", parts.Combo.Template.FindName("PART_Popup", parts.Combo) is Popup);
                results.Check($"{name}: the combo box shows its choice", parts.Combo.SelectionBoxItem is string);
                results.Check($"{name}: the text box has its text host", parts.Text.Template.FindName("PART_ContentHost", parts.Text) is ScrollViewer);
                parts.Text.Text = "typed";
                parts.Text.SelectAll();
                results.Check($"{name}: the text box still edits", parts.Text.SelectedText == "typed");

                var track = parts.Slider.Template.FindName("PART_Track", parts.Slider) as Track;
                results.Check($"{name}: the slider has its track", track is not null);
                results.Check($"{name}: the slider's thumb sits at its value", track?.Thumb is { } thumb && Math.Abs(thumb.TranslatePoint(default, track).X - (track.ActualWidth - thumb.ActualWidth) * 0.6) < 2);
                results.Check($"{name}: the vertical slider has its track", parts.Vertical.Template.FindName("PART_Track", parts.Vertical) is Track);

                var bars = Visuals<ScrollBar>(parts.Scroller).ToList();
                results.Check($"{name}: the scroll bar has its track", bars.Count > 0 && bars.All(b => b.Template.FindName("PART_Track", b) is Track));
                results.Check($"{name}: the vertical bar is narrow", bars.Any(b => b.Orientation == Orientation.Vertical && b.ActualWidth == 10 && b.ActualHeight > 10));
                results.Check($"{name}: the horizontal bar is low", bars.Any(b => b.Orientation == Orientation.Horizontal && b.ActualHeight == 10 && b.ActualWidth > 10));

                results.Check($"{name}: menu items are in the menu", Visuals<MenuItem>(parts.Menu).Count() == 4);
                results.Check($"{name}: a context menu has its template", new ContextMenu { Style = (Style)Application.Current.FindResource(typeof(ContextMenu)) }.Style?.Setters.Count > 0);
                if (render is not null) Render(window, System.IO.Path.ChangeExtension(render, $".{name}.png"));
            }
            finally
            {
                window.Close();
            }
        }
        return results.Finish();
    }

    private sealed record Parts(CheckBox SwitchOn, CheckBox SwitchOff, ComboBox Combo, TextBox Text, Slider Slider, Slider Vertical, ScrollViewer Scroller, Menu Menu);

    private static (Window, Parts) Gallery(Palette palette)
    {
        var board = new StackPanel { Margin = new Thickness(24) };
        void Line(string label, params UIElement[] controls)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            row.Children.Add(new TextBlock { Text = label, Width = 90, FontSize = 12, Foreground = palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center });
            foreach (var control in controls)
            {
                if (control is FrameworkElement element) element.Margin = new Thickness(0, 0, 16, 0);
                row.Children.Add(control);
            }
            board.Children.Add(row);
        }

        var parts = new Orbit.App.Settings.PageParts(palette);
        var on = parts.Toggle(true, _ => { });
        var off = parts.Toggle(false, _ => { });
        var disabled = parts.Toggle(true, _ => { });
        disabled.IsEnabled = false;
        Line("switch", on, off, disabled);

        Line("check box",
            new CheckBox { Content = "Off", FontSize = 13 },
            new CheckBox { Content = "On", IsChecked = true, FontSize = 13 },
            new CheckBox { Content = "Disabled", IsChecked = true, IsEnabled = false, FontSize = 13 },
            new CheckBox());

        var combo = new ComboBox { Width = 180, ItemsSource = new[] { "First choice", "Second choice" }, SelectedIndex = 0 };
        Line("combo box", combo, new ComboBox { Width = 120, IsEnabled = false, ItemsSource = new[] { "Disabled" }, SelectedIndex = 0 });

        var text = new TextBox { Width = 180, Padding = new Thickness(8, 5, 8, 5), Text = "Text in a field" };
        Line("text box", text, new TextBox { Width = 120, Padding = new Thickness(8, 5, 8, 5), Text = "Disabled", IsEnabled = false }, palette.PlainField(13));

        var slider = new Slider { Width = 180, Minimum = 0, Maximum = 100, Value = 60 };
        var vertical = new Slider { Orientation = Orientation.Vertical, Height = 60, Minimum = 0, Maximum = 100, Value = 30 };
        Line("slider", slider, new Slider { Width = 120, Value = 4, Maximum = 10, IsEnabled = false }, vertical);

        var scroller = new ScrollViewer
        {
            Width = 300, Height = 70, VerticalScrollBarVisibility = ScrollBarVisibility.Visible, HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            Content = new Border { Width = 600, Height = 300, Background = palette.Surface },
        };
        Line("scroll bar", scroller);

        var panel = new FrameworkElementFactory(typeof(StackPanel));
        var menu = new Menu { IsHitTestVisible = false, Background = Brushes.Transparent, Margin = new Thickness(4), Width = 200, ItemsPanel = new ItemsPanelTemplate(panel) };
        menu.Items.Add(new MenuItem { Header = "An item" });
        menu.Items.Add(new MenuItem { Header = "Checked", IsChecked = true, InputGestureText = "Ctrl+K" });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Disabled", IsEnabled = false });
        var submenu = new MenuItem { Header = "More" };
        submenu.Items.Add(new MenuItem { Header = "Inside" });
        menu.Items.Add(submenu);
        Line("menu", new Border { CornerRadius = new CornerRadius(10), Background = palette.Canvas, BorderBrush = palette.Hairline, BorderThickness = new Thickness(1), Child = menu });

        var window = new Window
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, Width = 720, Height = 680,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Background = palette.Canvas, Content = board,
            FontFamily = (FontFamily)Application.Current.FindResource("OrbitTextFont"),
        };
        palette.ApplyTo(window.Resources);
        return (window, new Parts(on, off, combo, text, slider, vertical, scroller, menu));
    }

    private static double Knob(CheckBox box) =>
        Visuals<System.Windows.Shapes.Ellipse>(box).FirstOrDefault() is { } knob ? knob.TranslatePoint(default, box).X : -1;

    private static bool Wait(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddMilliseconds(600);
        while (!condition() && DateTime.UtcNow < until)
        {
            Pump();
            Thread.Sleep(15);
        }
        return condition();
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var deeper in Visuals<T>(child)) yield return deeper;
        }
    }
}
