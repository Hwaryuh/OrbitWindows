using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbit.Core.Capture;
using Orbit.Core.Display;

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

    /// <summary>A picture as 32-bit BGRA pixels, for the steps that work on frames (text recognition).</summary>
    public static ScreenFrame ToFrame(BitmapSource bitmap)
    {
        BitmapSource source = bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Bgr32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        return new ScreenFrame(pixels, source.PixelWidth, source.PixelHeight, stride, new PixelRect(0, 0, source.PixelWidth, source.PixelHeight), "File");
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
        return Clipboards.Set(data);
    }
}
