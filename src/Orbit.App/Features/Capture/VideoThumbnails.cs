using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbit.Core.Launcher;

namespace Orbit.App.Features.Capture;

/// <summary>A recording's picture as File Explorer shows it (Windows makes MP4 thumbnails itself).</summary>
internal static class VideoThumbnails
{
    private const int Size = 480;

    /// <summary>On the calling thread, which must be a single-threaded apartment (the UI thread is).</summary>
    public static BitmapSource? Read(string path)
    {
        if (ShellImage.Read(path, Size, iconOnly: false) is not { } pixels) return null;
        // Video frames have no alpha; the shell leaves it at zero.
        var bitmap = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Bgr32, null, pixels.Pixels, pixels.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Off the UI thread, on a short-lived thread of its own apartment, as the shell wants.</summary>
    public static Task<BitmapSource?> ReadAsync(string path)
    {
        var done = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(Read(path));
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        }) { IsBackground = true, Name = "Orbit video thumbnail" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
