using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Orbit.Core.Display;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Orbit.App.Overlays;

/// <summary>
/// A borderless topmost window placed in physical pixels. Two kinds: non-activating (ring, HUDs:
/// never takes focus, optionally click-through) and activating (capture overlay: needs the keys).
/// Always place it with <see cref="Place"/>, never with Left/Top, which are DIPs of whichever
/// monitor WPF thinks the window is on.
/// </summary>
public class OverlayWindow : Window
{
    private static readonly HWND InsertTopmost = new(-1);

    private readonly bool _activating;
    private readonly bool _clickThrough;
    private PixelRect _placement;

    protected OverlayWindow(bool activating, bool transparent, bool clickThrough)
    {
        // WS_EX_TRANSPARENT passes clicks through only a layered window, which WPF makes only for AllowsTransparency.
        if (clickThrough && !transparent) throw new ArgumentException("A click-through overlay must be transparent", nameof(clickThrough));
        _activating = activating;
        _clickThrough = clickThrough;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = activating;
        AllowsTransparency = transparent;
        Background = transparent ? Brushes.Transparent : Brushes.Black;
        SourceInitialized += (_, _) => ApplyStyles();
    }

    public HWND Handle => new(new WindowInteropHelper(this).EnsureHandle());

    /// <summary>Where the window is, in physical pixels.</summary>
    public PixelRect Placement => _placement;

    public void Place(PixelRect rect)
    {
        _placement = rect;
        var flags = SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER;
        if (!_activating) flags |= SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        PInvoke.SetWindowPos(Handle, InsertTopmost, rect.X, rect.Y, rect.Width, rect.Height, flags);
    }

    /// <summary>Shows the window at <paramref name="rect"/>; the handle exists first, so it never flashes elsewhere.</summary>
    public void ShowAt(PixelRect rect)
    {
        Place(rect);
        Show();
        Place(rect);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // WPF resized the window to Windows' suggestion for the new monitor; put it back where Orbit wants it.
        if (!_placement.IsEmpty) Dispatcher.BeginInvoke(() => Place(_placement));
        OnMonitorDpiChanged(oldDpi, newDpi);
    }

    protected virtual void OnMonitorDpiChanged(DpiScale oldDpi, DpiScale newDpi) { }

    private void ApplyStyles()
    {
        var style = (WINDOW_EX_STYLE)PInvoke.GetWindowLong(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        style |= WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_TOPMOST;
        if (!_activating) style |= WINDOW_EX_STYLE.WS_EX_NOACTIVATE;
        if (_clickThrough) style |= WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        PInvoke.SetWindowLong(Handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (int)style);
    }
}
