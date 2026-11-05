using System.Windows;
using System.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Graphics.Dwm;

namespace Orbit.App.Overlays;

/// <summary>The title bar of a window with a dark body.</summary>
internal static class TitleBars
{
    /// <summary>Call once the window has its handle (<see cref="Window.SourceInitialized"/>).</summary>
    public static unsafe void MakeDark(Window window)
    {
        int on = 1;
        PInvoke.DwmSetWindowAttribute(new(new WindowInteropHelper(window).Handle), DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, &on, sizeof(int));
    }
}
