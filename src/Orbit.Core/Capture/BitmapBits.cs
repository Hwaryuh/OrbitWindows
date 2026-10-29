using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace Orbit.Core.Capture;

/// <summary>A GDI bitmap's pixels as 32-bit BGRA, top-down. The bitmap must not be selected into a DC.</summary>
internal static class BitmapBits
{
    /// <returns>null if GetDIBits failed.</returns>
    public static unsafe byte[]? Read(HDC dc, HBITMAP bitmap, int width, int height)
    {
        var header = new BITMAPINFO();
        header.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        header.bmiHeader.biWidth = width;
        header.bmiHeader.biHeight = -height; // top-down
        header.bmiHeader.biPlanes = 1;
        header.bmiHeader.biBitCount = 32;
        header.bmiHeader.biCompression = 0; // BI_RGB
        var pixels = new byte[width * height * 4];
        fixed (byte* p = pixels)
            return PInvoke.GetDIBits(dc, bitmap, 0, (uint)height, p, &header, DIB_USAGE.DIB_RGB_COLORS) == 0 ? null : pixels;
    }
}
