using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbit.Core.Capture;

namespace Orbit.App.Features.Capture;

/// <summary>Captured frames as WPF pictures, PNG files and clipboard images.</summary>
internal static class FrameImages
{
    /// <summary>A bitmap at <paramref name="dpi"/>: at the monitor's DPI, WPF draws it one image pixel per screen pixel.</summary>
    public static BitmapSource ToBitmap(ScreenFrame frame, double dpi = 96)
    {
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, dpi, dpi, PixelFormats.Bgr32, null, frame.Pixels, frame.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] Png(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// The picture on the clipboard as a bitmap and as PNG (Office, browsers and chat apps take the
    /// PNG and keep it sharp). False if another app held the clipboard the whole time.
    /// </summary>
    public static bool Copy(BitmapSource bitmap, byte[] png)
    {
        var data = new DataObject();
        data.SetImage(bitmap);
        data.SetData("PNG", new MemoryStream(png));
        return Clipboards.TrySet(() => Clipboard.SetDataObject(data, copy: true));
    }
}

/// <summary>Writing the clipboard, which another app may hold for a moment.</summary>
internal static class Clipboards
{
    /// <summary>Text onto the clipboard, then a toast: <paramref name="message"/>, or that copying failed.</summary>
    public static bool CopyText(string text, string message, string? detail = null)
    {
        bool copied = TrySet(() => Clipboard.SetText(text));
        Overlays.Toast.Show(copied ? message : Strings.CopyFailed, copied ? detail : null);
        return copied;
    }

    public static bool TrySet(Action set)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                set();
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(30);
            }
        }
        return false;
    }
}
