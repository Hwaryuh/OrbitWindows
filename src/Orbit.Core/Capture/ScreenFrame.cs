using Orbit.Core.Display;

namespace Orbit.Core.Capture;

/// <summary>
/// A captured image: 32-bit BGRA, top-down, in physical pixels. <see cref="Bounds"/> is where it
/// was on the virtual screen.
/// </summary>
public sealed class ScreenFrame(byte[] pixels, int width, int height, int stride, PixelRect bounds, string source)
{
    public byte[] Pixels { get; } = pixels;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Stride { get; } = stride;
    public PixelRect Bounds { get; } = bounds;
    /// <summary>"WGC" or "GDI", for the log.</summary>
    public string Source { get; } = source;

    /// <summary>The part of this frame inside <paramref name="area"/> (virtual-screen pixels).</summary>
    public ScreenFrame Crop(PixelRect area)
    {
        var inside = area.Intersect(Bounds);
        if (inside.IsEmpty) throw new ArgumentException($"{area} is outside {Bounds}", nameof(area));
        var local = inside.RelativeTo(Bounds);
        int stride = local.Width * 4;
        var pixels = new byte[stride * local.Height];
        for (int row = 0; row < local.Height; row++)
            Buffer.BlockCopy(Pixels, (local.Y + row) * Stride + local.X * 4, pixels, row * stride, stride);
        return new ScreenFrame(pixels, local.Width, local.Height, stride, inside, Source);
    }
}

public static class FrameCompare
{
    /// <summary>
    /// The share of pixels whose blue, green and red match within <paramref name="tolerance"/>.
    /// Alpha is ignored: GDI leaves it 0. Frames of different sizes don't match at all.
    /// </summary>
    public static double MatchRatio(ScreenFrame a, ScreenFrame b, int tolerance = 0)
    {
        if (a.Width != b.Width || a.Height != b.Height) return 0;
        long same = 0;
        for (int y = 0; y < a.Height; y++)
        {
            var rowA = a.Pixels.AsSpan(y * a.Stride, a.Width * 4);
            var rowB = b.Pixels.AsSpan(y * b.Stride, b.Width * 4);
            for (int x = 0; x < rowA.Length; x += 4)
            {
                if (Math.Abs(rowA[x] - rowB[x]) <= tolerance &&
                    Math.Abs(rowA[x + 1] - rowB[x + 1]) <= tolerance &&
                    Math.Abs(rowA[x + 2] - rowB[x + 2]) <= tolerance)
                    same++;
            }
        }
        return (double)same / ((long)a.Width * a.Height);
    }
}
