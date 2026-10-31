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
        frame = Prepare(frame, (int)OcrEngine.MaxImageDimension);
        // Larger pictures are refused; halving keeps the text readable for the engine.
        while (frame.Width > OcrEngine.MaxImageDimension || frame.Height > OcrEngine.MaxImageDimension)
            frame = Halve(frame);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(frame.Pixels.AsBuffer(), BitmapPixelFormat.Bgra8, frame.Width, frame.Height, BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
    }

    /// <summary>
    /// A small selection made readable for the engine, which reads nothing from a tight crop of one
    /// line of screen text: twice the size (smooth, not blocky) and an 8 px margin in the picture's
    /// own edge color. Measured on 12 rendered lines of 11–13 px English and Korean: plain 0 of 12
    /// read exactly, this 8; 3x and 4x, or wider margins, did worse. Large pictures pass unchanged.
    /// </summary>
    internal static ScreenFrame Prepare(ScreenFrame frame, int maxDimension)
    {
        const int Factor = 2, Margin = 8;
        if (frame.Width == 0 || frame.Height == 0 || frame.Height >= 200) return frame;
        if (frame.Width * Factor + 2 * Margin > maxDimension || frame.Height * Factor + 2 * Margin > maxDimension) return frame;
        return Pad(Enlarge(frame, Factor), Margin);
    }

    /// <summary><paramref name="factor"/> times larger, each new pixel blended from its four nearest (bilinear).</summary>
    internal static ScreenFrame Enlarge(ScreenFrame frame, int factor)
    {
        int width = frame.Width * factor, height = frame.Height * factor, stride = width * 4;
        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            double sy = Math.Clamp((y + 0.5) / factor - 0.5, 0, frame.Height - 1);
            int y0 = (int)sy, y1 = Math.Min(y0 + 1, frame.Height - 1);
            double fy = sy - y0;
            for (int x = 0; x < width; x++)
            {
                double sx = Math.Clamp((x + 0.5) / factor - 0.5, 0, frame.Width - 1);
                int x0 = (int)sx, x1 = Math.Min(x0 + 1, frame.Width - 1);
                double fx = sx - x0;
                for (int c = 0; c < 4; c++)
                {
                    double top = frame.Pixels[y0 * frame.Stride + x0 * 4 + c] * (1 - fx) + frame.Pixels[y0 * frame.Stride + x1 * 4 + c] * fx;
                    double bottom = frame.Pixels[y1 * frame.Stride + x0 * 4 + c] * (1 - fx) + frame.Pixels[y1 * frame.Stride + x1 * 4 + c] * fx;
                    pixels[y * stride + x * 4 + c] = (byte)Math.Round(top * (1 - fy) + bottom * fy);
                }
            }
        }
        return new ScreenFrame(pixels, width, height, stride, frame.Bounds, frame.Source);
    }

    /// <summary>A border of <paramref name="margin"/> pixels in the average color of the picture's outermost pixels.</summary>
    internal static ScreenFrame Pad(ScreenFrame frame, int margin)
    {
        var sum = new long[4];
        long count = 0;
        void Add(int x, int y)
        {
            for (int c = 0; c < 4; c++) sum[c] += frame.Pixels[y * frame.Stride + x * 4 + c];
            count++;
        }
        for (int x = 0; x < frame.Width; x++)
        {
            Add(x, 0);
            Add(x, frame.Height - 1);
        }
        for (int y = 0; y < frame.Height; y++)
        {
            Add(0, y);
            Add(frame.Width - 1, y);
        }
        int width = frame.Width + 2 * margin, height = frame.Height + 2 * margin, stride = width * 4;
        var pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += 4)
            for (int c = 0; c < 4; c++) pixels[i + c] = (byte)(sum[c] / count);
        for (int y = 0; y < frame.Height; y++)
            Buffer.BlockCopy(frame.Pixels, y * frame.Stride, pixels, (y + margin) * stride + margin * 4, frame.Width * 4);
        return new ScreenFrame(pixels, width, height, stride, frame.Bounds, frame.Source);
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
