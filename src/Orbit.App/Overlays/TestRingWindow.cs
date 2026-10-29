using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Orbit.Core;
using Orbit.Core.Display;

namespace Orbit.App.Overlays;

/// <summary>
/// M0 spike: a stand-in for the ring (340 × 340 points, as OrbitRing.swift) that follows the
/// pointer while the trigger is held, to see placement and DPI changes across monitors.
/// </summary>
public sealed class TestRingWindow : OverlayWindow
{
    private const double SizeDip = 340;

    private readonly TextBlock _label;
    private MonitorInfo? _monitor;
    private int _lastX = int.MinValue, _lastY = int.MinValue;

    public TestRingWindow() : base(activating: false, transparent: true, clickThrough: true)
    {
        var ring = new Ellipse
        {
            Width = SizeDip, Height = SizeDip,
            Stroke = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(150, 10, 10, 10)),
        };
        var center = new Ellipse { Width = 8, Height = 8, Fill = Brushes.White };
        _label = new TextBlock { Foreground = Brushes.White, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
        var grid = new Grid { Width = SizeDip, Height = SizeDip };
        grid.Children.Add(ring);
        grid.Children.Add(center);
        grid.Children.Add(new Border { Child = _label, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 70) });
        Content = grid;
    }

    public void Open(int x, int y)
    {
        Follow(x, y, force: true);
        Show();
        Follow(x, y, force: true);
        CompositionTarget.Rendering += OnRendering;
        Log.Info($"Test ring opened at ({x},{y}) on {_monitor}");
    }

    public void Release()
    {
        CompositionTarget.Rendering -= OnRendering;
        Log.Info($"Test ring released at ({_lastX},{_lastY}) on {_monitor}");
        Close();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var (x, y) = Monitors.Cursor();
        Follow(x, y, force: false);
    }

    private void Follow(int x, int y, bool force)
    {
        if (!force && x == _lastX && y == _lastY) return;
        _lastX = x;
        _lastY = y;
        var monitor = Monitors.FromPoint(x, y);
        if (monitor is null) return;
        if (monitor.Handle != _monitor?.Handle)
        {
            if (_monitor is not null) Log.Info($"Test ring moved from {_monitor.DeviceName} ({_monitor.Dpi}dpi) to {monitor.DeviceName} ({monitor.Dpi}dpi)");
            _monitor = monitor;
            _label.Text = $"{monitor.Dpi} DPI · {monitor.Scale:P0}";
        }
        int size = (int)Math.Round(SizeDip * monitor.Scale);
        Place(new PixelRect(x - size / 2, y - size / 2, size, size));
    }

    protected override void OnMonitorDpiChanged(DpiScale oldDpi, DpiScale newDpi) =>
        Log.Info($"Test ring DPI changed {oldDpi.PixelsPerInchX} -> {newDpi.PixelsPerInchX}, placement {Placement}");
}
