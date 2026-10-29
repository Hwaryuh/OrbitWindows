using Orbit.Core.Display;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Orbit.Core.Capture;

/// <summary>
/// BitBlt from the screen: the fallback when Windows.Graphics.Capture fails, and the reference
/// the capture spike compares against. Misses some protected and hardware-overlay content.
/// </summary>
public static class GdiCapture
{
    public static unsafe ScreenFrame Capture(PixelRect area)
    {
        var screen = PInvoke.GetDC(HWND.Null);
        var memory = PInvoke.CreateCompatibleDC(screen);
        var bitmap = PInvoke.CreateCompatibleBitmap(screen, area.Width, area.Height);
        var previous = PInvoke.SelectObject(memory, bitmap);
        try
        {
            if (!PInvoke.BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y,
                    ROP_CODE.SRCCOPY | ROP_CODE.CAPTUREBLT))
                throw new InvalidOperationException("BitBlt failed");

            PInvoke.SelectObject(memory, previous);
            var pixels = BitmapBits.Read(memory, bitmap, area.Width, area.Height) ?? throw new InvalidOperationException("GetDIBits failed");
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return new ScreenFrame(pixels, area.Width, area.Height, area.Width * 4, area, "GDI");
        }
        finally
        {
            PInvoke.SelectObject(memory, previous);
            PInvoke.DeleteObject(bitmap);
            PInvoke.DeleteDC(memory);
            PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }
}
