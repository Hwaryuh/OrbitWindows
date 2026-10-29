using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Orbit.Core.Capture;
using Orbit.Core.Display;

namespace Orbit.App.Overlays;

/// <summary>
/// One monitor's capture overlay: an opaque window that shows the frozen frame pixel for pixel,
/// dimmed outside the selection. The selection is read from the pointer in physical pixels, so
/// no DIP rounding gets into the captured area.
/// </summary>
public sealed class CaptureOverlayWindow : OverlayWindow
{
    private readonly MonitorInfo _monitor;
    private readonly RectangleGeometry _hole = new();
    private readonly Rectangle _outline;
    private (int X, int Y)? _start;

    public CaptureOverlayWindow(MonitorInfo monitor, ScreenFrame frame) : base(activating: true, transparent: false, clickThrough: false)
    {
        _monitor = monitor;
        Cursor = Cursors.Cross;

        double width = monitor.ToDip(monitor.Bounds.Width), height = monitor.ToDip(monitor.Bounds.Height);
        var image = new Image { Source = Features.Capture.FrameImages.ToBitmap(frame, monitor.Dpi), Stretch = Stretch.None };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

        var dim = new Path
        {
            Fill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)),
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, width, height)), _hole),
        };
        _outline = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1, Visibility = Visibility.Collapsed };

        var canvas = new Canvas { Width = width, Height = height };
        canvas.Children.Add(image);
        canvas.Children.Add(dim);
        canvas.Children.Add(_outline);
        Content = canvas;

        MouseLeftButtonDown += (_, _) => { _start = Monitors.Cursor(); CaptureMouse(); Update(); };
        MouseMove += (_, _) => { if (_start is not null) Update(); };
        MouseLeftButtonUp += (_, _) => Finish();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Cancelled?.Invoke(); };
    }

    /// <summary>A selection in virtual-screen physical pixels.</summary>
    public event Action<PixelRect>? Selected;
    public event Action? Cancelled;

    public MonitorInfo Monitor => _monitor;

    private PixelRect CurrentSelection()
    {
        if (_start is not { } start) return default;
        var (x, y) = Monitors.Cursor();
        return PixelRect.FromCorners(start.X, start.Y, x, y).Intersect(_monitor.Bounds);
    }

    private void Update()
    {
        var local = CurrentSelection().RelativeTo(_monitor.Bounds);
        var rect = new Rect(_monitor.ToDip(local.X), _monitor.ToDip(local.Y), _monitor.ToDip(local.Width), _monitor.ToDip(local.Height));
        _hole.Rect = rect;
        Canvas.SetLeft(_outline, rect.X);
        Canvas.SetTop(_outline, rect.Y);
        _outline.Width = rect.Width;
        _outline.Height = rect.Height;
        _outline.Visibility = Visibility.Visible;
    }

    private void Finish()
    {
        if (_start is null) return;
        var selection = CurrentSelection();
        _start = null;
        ReleaseMouseCapture();
        if (selection.Width < 2 || selection.Height < 2) return;
        Selected?.Invoke(selection);
    }

}
