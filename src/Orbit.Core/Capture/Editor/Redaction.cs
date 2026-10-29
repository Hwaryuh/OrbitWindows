namespace Orbit.Core.Capture.Editor;

/// <summary>
/// Blur and pixelate for the editor, on the CPU, over 32-bit BGRA pixels (Orbit: Core Image's
/// gaussian blur and pixellate in <c>EditorModel.redacted</c>). The whole picture is processed once
/// per strength; marks show it through their shape. Edges repeat the border pixel, as Core
/// Image's <c>clampedToExtent</c>.
/// </summary>
public static class Redaction
{
    /// <summary>Orbit's blur radius for a strength of 1–10 on a picture this many pixels wide.</summary>
    public static double BlurRadius(int level, int pixelWidth) => level * Math.Max(2, pixelWidth / 400.0);

    /// <summary>Orbit's pixelate cell size for a strength of 1–10 on a picture this many pixels wide.</summary>
    public static int PixelCell(int level, int pixelWidth) => Math.Max(1, (int)Math.Round(level * Math.Max(3, pixelWidth / 450.0)));

    /// <summary>A Gaussian blur of standard deviation <paramref name="sigma"/>, as three box blurs.</summary>
    public static byte[] Blur(byte[] pixels, int width, int height, int stride, double sigma)
    {
        var a = (byte[])pixels.Clone();
        if (sigma <= 0 || width == 0 || height == 0) return a;
        var b = new byte[a.Length];
        foreach (int size in BoxSizes(sigma, 3))
        {
            int radius = (size - 1) / 2;
            BoxPass(a, b, width, height, stride, radius, horizontal: true);
            BoxPass(b, a, width, height, stride, radius, horizontal: false);
        }
        return a;
    }

    /// <summary>Square cells of <paramref name="cell"/> pixels from the top left, each its average color.</summary>
    public static byte[] Pixelate(byte[] pixels, int width, int height, int stride, int cell)
    {
        var result = (byte[])pixels.Clone();
        cell = Math.Max(1, cell);
        Span<long> sum = stackalloc long[4];
        for (int y0 = 0; y0 < height; y0 += cell)
        {
            int y1 = Math.Min(height, y0 + cell);
            for (int x0 = 0; x0 < width; x0 += cell)
            {
                int x1 = Math.Min(width, x0 + cell);
                sum.Clear();
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                        for (int c = 0; c < 4; c++) sum[c] += pixels[y * stride + x * 4 + c];
                int n = (x1 - x0) * (y1 - y0);
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                        for (int c = 0; c < 4; c++) result[y * stride + x * 4 + c] = (byte)((sum[c] + n / 2) / n);
            }
        }
        return result;
    }

    /// <summary>Odd box widths whose repeated averaging approaches a Gaussian (W. Jarosz, "Fast image convolutions").</summary>
    internal static int[] BoxSizes(double sigma, int passes)
    {
        double ideal = Math.Sqrt(12 * sigma * sigma / passes + 1);
        int lower = (int)Math.Floor(ideal);
        if (lower % 2 == 0) lower--;
        int upper = lower + 2;
        double mIdeal = (12 * sigma * sigma - passes * lower * lower - 4 * passes * lower - 3 * passes) / (-4.0 * lower - 4);
        int m = (int)Math.Round(mIdeal);
        return Enumerable.Range(0, passes).Select(i => i < m ? lower : upper).ToArray();
    }

    /// <summary>A moving average of 2 × <paramref name="radius"/> + 1 pixels along rows or columns.</summary>
    private static void BoxPass(byte[] source, byte[] target, int width, int height, int stride, int radius, bool horizontal)
    {
        int lines = horizontal ? height : width, length = horizontal ? width : height;
        int step = horizontal ? 4 : stride;
        int window = radius * 2 + 1;
        Parallel.For(0, lines, line =>
        {
            int origin = horizontal ? line * stride : line * 4;
            for (int c = 0; c < 4; c++)
            {
                int Value(int i) => source[origin + Math.Clamp(i, 0, length - 1) * step + c];
                int sum = 0;
                for (int i = -radius; i <= radius; i++) sum += Value(i);
                for (int i = 0; i < length; i++)
                {
                    target[origin + i * step + c] = (byte)((sum + window / 2) / window);
                    sum += Value(i + radius + 1) - Value(i - radius);
                }
            }
        });
    }
}
