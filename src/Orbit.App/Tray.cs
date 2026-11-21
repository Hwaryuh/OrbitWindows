using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;

namespace Orbit.App;

/// <summary>Orbit's tray icon: its menu and its notifications.</summary>
internal sealed class Tray : IDisposable
{
    private readonly TaskbarIcon _icon;

    /// <param name="items">Menu items in order; a null entry is a separator.</param>
    public Tray(IEnumerable<(string Header, Action Action)?> items)
    {
        var menu = new ContextMenu();
        foreach (var item in items)
            menu.Items.Add(item is { } entry ? Design.Controls.MenuItem(entry.Header, entry.Action) : new Separator());

        _icon = new TaskbarIcon
        {
            ToolTipText = Strings.AppName,
            Icon = DrawIcon(),
            ContextMenu = menu,
            MenuActivation = H.NotifyIcon.Core.PopupActivationMode.RightClick,
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    public void Notify(string title, string message) => _icon.ShowNotification(title, message);

    public void Dispose() => _icon.Dispose();

    public void FollowTaskbarTheme() => _icon.Icon = DrawIcon();

    private static System.Drawing.Icon DrawIcon()
    {
        var dpi = VisualTreeHelper.GetDpi(new DrawingVisual());
        int pixels = (int)Math.Round(SystemParameters.SmallIconWidth * dpi.DpiScaleX);
        var theme = TaskbarIsLight() ? Design.Palette.Light : Design.Palette.DarkTheme;
        var mark = new Design.OrbitMark { Size = pixels, Ink = theme.Ink, Accent = theme.Accent, Dot = true };
        mark.Measure(new Size(pixels, pixels));
        mark.Arrange(new Rect(0, 0, pixels, pixels));
        var bitmap = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(mark);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);

        byte side = (byte)(pixels >= 256 ? 0 : pixels);
        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((short)0); writer.Write((short)1); writer.Write((short)1);
            writer.Write(side); writer.Write(side); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((short)1); writer.Write((short)32);
            writer.Write((int)png.Length); writer.Write(6 + 16);
            writer.Write(png.ToArray());
        }
        ico.Position = 0;
        return new System.Drawing.Icon(ico);
    }

    private static bool TaskbarIsLight()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }
}
