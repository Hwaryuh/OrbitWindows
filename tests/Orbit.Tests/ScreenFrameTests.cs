using Orbit.Core.Capture;
using Orbit.Core.Display;

namespace Orbit.Tests;

public class ScreenFrameTests
{
    /// <summary>A frame whose pixel (x, y) has blue = x and green = y, at a virtual-screen origin.</summary>
    private static ScreenFrame Gradient(int originX, int originY, int width, int height, int padding = 0)
    {
        int stride = width * 4 + padding;
        var pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * stride + x * 4;
                pixels[i] = (byte)x;
                pixels[i + 1] = (byte)y;
                pixels[i + 3] = 255;
            }
        return new ScreenFrame(pixels, width, height, stride, new PixelRect(originX, originY, width, height), "TEST");
    }

    [Fact]
    public void CropUsesVirtualScreenCoordinatesOnANegativeMonitor()
    {
        // The left monitor of this PC: 1080 wide at x = -1080.
        var frame = Gradient(-1080, 0, 200, 100);
        var crop = frame.Crop(new PixelRect(-1070, 5, 20, 10));
        Assert.Equal(20, crop.Width);
        Assert.Equal(10, crop.Height);
        Assert.Equal(new PixelRect(-1070, 5, 20, 10), crop.Bounds);
        Assert.Equal(10, crop.Pixels[0]);      // blue = local x
        Assert.Equal(5, crop.Pixels[1]);       // green = local y
    }

    [Fact]
    public void CropHonorsSourceStride()
    {
        var frame = Gradient(0, 0, 50, 50, padding: 12);
        var crop = frame.Crop(new PixelRect(3, 4, 5, 6));
        Assert.Equal(5 * 4, crop.Stride);
        Assert.Equal(3, crop.Pixels[0]);
        Assert.Equal(4 + 5, crop.Pixels[5 * crop.Stride + 1]);
    }

    [Fact]
    public void CropIsClippedToTheFrame()
    {
        var frame = Gradient(0, 0, 10, 10);
        var crop = frame.Crop(new PixelRect(-5, -5, 8, 8));
        Assert.Equal(new PixelRect(0, 0, 3, 3), crop.Bounds);
    }

    [Fact]
    public void CropOutsideThrows()
    {
        var frame = Gradient(0, 0, 10, 10);
        Assert.Throws<ArgumentException>(() => frame.Crop(new PixelRect(20, 20, 5, 5)));
    }

    [Fact]
    public void MatchIgnoresAlphaAndStride()
    {
        var a = Gradient(0, 0, 30, 20);
        var b = Gradient(0, 0, 30, 20, padding: 8);
        for (int i = 3; i < b.Pixels.Length; i += 4) b.Pixels[i] = 0;
        Assert.Equal(1.0, FrameCompare.MatchRatio(a, b));
    }

    [Fact]
    public void MatchCountsDifferentPixels()
    {
        var a = Gradient(0, 0, 10, 10);
        var b = Gradient(0, 0, 10, 10);
        b.Pixels[2] = 200; // red of pixel (0, 0)
        Assert.Equal(0.99, FrameCompare.MatchRatio(a, b), 6);
        Assert.Equal(1.0, FrameCompare.MatchRatio(a, b, tolerance: 255));
    }

    [Fact]
    public void FramesOfDifferentSizesDoNotMatch()
    {
        Assert.Equal(0, FrameCompare.MatchRatio(Gradient(0, 0, 10, 10), Gradient(0, 0, 10, 11)));
    }
}
