using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Orbit.Core.Capture;

/// <summary>
/// Text in a capture, through Windows.Media.Ocr (Orbit: Modules/OCR/OCR.swift over Vision), in the
/// languages of the user's profile. Windows reads one language per engine; the profile's first
/// language that has an OCR pack is used, as <c>TryCreateFromUserProfileLanguages</c> picks it.
/// </summary>
public static class TextRecognition
{
    /// <summary>False when no language on this PC has an OCR pack: Settings &gt; Time &amp; language adds one.</summary>
    public static bool IsAvailable => OcrEngine.AvailableRecognizerLanguages.Count > 0;

    /// <summary>The text, one line per line found; empty if none; null when no OCR language is installed.</summary>
    public static async Task<string?> RecognizeAsync(ScreenFrame frame)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) return null;
        // Larger pictures are refused; halving keeps the text readable for the engine.
        while (frame.Width > OcrEngine.MaxImageDimension || frame.Height > OcrEngine.MaxImageDimension)
            frame = Halve(frame);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(frame.Pixels.AsBuffer(), BitmapPixelFormat.Bgra8, frame.Width, frame.Height, BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
    }

    /// <summary>Half the size, each pixel the average of four.</summary>
    internal static ScreenFrame Halve(ScreenFrame frame)
    {
        int width = Math.Max(1, frame.Width / 2), height = Math.Max(1, frame.Height / 2), stride = width * 4;
        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            int y0 = Math.Min(y * 2, frame.Height - 1), y1 = Math.Min(y * 2 + 1, frame.Height - 1);
            for (int x = 0; x < width; x++)
            {
                int x0 = Math.Min(x * 2, frame.Width - 1) * 4, x1 = Math.Min(x * 2 + 1, frame.Width - 1) * 4;
                for (int c = 0; c < 4; c++)
                {
                    int sum = frame.Pixels[y0 * frame.Stride + x0 + c] + frame.Pixels[y0 * frame.Stride + x1 + c]
                            + frame.Pixels[y1 * frame.Stride + x0 + c] + frame.Pixels[y1 * frame.Stride + x1 + c];
                    pixels[y * stride + x * 4 + c] = (byte)(sum / 4);
                }
            }
        }
        return new ScreenFrame(pixels, width, height, stride, frame.Bounds, frame.Source);
    }
}
