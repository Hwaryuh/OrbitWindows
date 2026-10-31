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

    /// <summary>
    /// Closes the menu when a mouse button goes down anywhere outside it. A menu on a window that
    /// never takes focus (the capture cards) otherwise stays open until one of its items is
    /// picked: WPF closes menus when their window loses focus, and that window never had it.
    /// While the menu is open each button is looked at every 20 ms; nothing runs otherwise.
    /// </summary>
    public static ContextMenu CloseOnOutsidePress(this ContextMenu menu)
    {
        System.Windows.Threading.DispatcherTimer? timer = null;
        var wasDown = new bool[MouseButtons.Length];
        menu.Opened += (_, _) =>
        {
            timer?.Stop();
            // The right press that opened the menu may still be down; reading the buttons once also
            // clears their "pressed since last asked" bit.
            for (int i = 0; i < MouseButtons.Length; i++) wasDown[i] = Pressed(MouseButtons[i], out _);
            timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                bool fresh = false;
                for (int i = 0; i < MouseButtons.Length; i++)
                {
                    bool down = Pressed(MouseButtons[i], out bool since);
                    // A new press of any one button, or a click short enough to fall between two looks.
                    if ((down && !wasDown[i]) || (since && !down)) fresh = true;
                    wasDown[i] = down;
                }
                if (fresh && !PointerOver(menu)) menu.IsOpen = false;
            };
            timer.Start();
        };
        menu.Closed += (_, _) =>
        {
            timer?.Stop();
            timer = null;
        };
        return menu;
    }

    private static readonly int[] MouseButtons = [0x01, 0x02, 0x04]; // left, right, middle

    /// <summary>Whether the button is down now; <paramref name="since"/>: pressed since the last look (best effort, as Windows keeps it).</summary>
    private static bool Pressed(int vk, out bool since)
    {
        short state = Windows.Win32.PInvoke.GetAsyncKeyState(vk);
        since = (state & 1) != 0;
        return (state & 0x8000) != 0;
    }

    /// <summary>Whether the pointer is over the menu's own window (its popup).</summary>
    private static bool PointerOver(ContextMenu menu)
    {
        if (PresentationSource.FromVisual(menu) is not System.Windows.Interop.HwndSource source) return true;
        Windows.Win32.PInvoke.GetWindowRect(new Windows.Win32.Foundation.HWND(source.Handle), out var r);
        var (x, y) = Orbit.Core.Display.Monitors.Cursor();
        return x >= r.left && x < r.right && y >= r.top && y < r.bottom;
    }
}
