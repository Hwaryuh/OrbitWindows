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

    /// <summary>A plain ring on black until Orbit's icon is drawn: a PNG wrapped in a one-image .ico.</summary>
    private static System.Drawing.Icon DrawIcon()
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawEllipse(Brushes.Black, null, new Point(16, 16), 15, 15);
            context.DrawEllipse(null, new Pen(Brushes.White, 3), new Point(16, 16), 9, 9);
        }
        var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);

        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((short)0); writer.Write((short)1); writer.Write((short)1);  // ICONDIR: icon, 1 image
            writer.Write((byte)32); writer.Write((byte)32); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((short)1); writer.Write((short)32);                         // planes, bits per pixel
            writer.Write((int)png.Length); writer.Write(6 + 16);                     // size, offset
            writer.Write(png.ToArray());
        }
        ico.Position = 0;
        return new System.Drawing.Icon(ico);
    }
}
