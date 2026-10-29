using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Orbit.App.Design;
using Orbit.Core.Capture.Editor;

namespace Orbit.App.Features.Capture.Editor;

/// <summary>
/// The picture in the editor: fitted to the window, zoomed (0.25–8) and panned, with the mouse
/// turned into the model's drags (Orbit: <c>canvasArea</c>, <c>canvasGesture</c>).
/// </summary>
internal sealed class EditorCanvas : FrameworkElement
{
    private const double Room = 32;
    private static readonly Pen Edge = new Pen(Palette.Fade(Brushes.White, 0.08), 1).Frozen();

    private readonly EditorWindow _window;
    private double _zoom = 1;
    private Vector _pan;
    private Point? _panFrom;
    private MouseButton _panButton;
    private Vector _panStart;
    private PointD? _dragStart;
    private int _clicks;
    private Point? _hover;

    public EditorCanvas(EditorWindow window)
    {
        _window = window;
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        SizeChanged += (_, _) => Changed();
    }

    private EditorModel Model => _window.Model;

    /// <summary>Space held: a drag moves the picture instead of drawing.</summary>
    public bool Panning
    {
        get;
        set
        {
            field = value;
            Cursor = ToolCursor();
        }
    }

    /// <summary>Screen DIPs per point.</summary>
    public double Scale
    {
        get
        {
            double fit = Math.Min(1, Math.Min((ActualWidth - Room * 2) / Model.Width, (ActualHeight - Room * 2) / Model.Height));
            return Math.Max(0.01, fit) * _zoom;
        }
    }

    /// <summary>Where the picture's top left is on the canvas.</summary>
    public Point Origin => new(ActualWidth / 2 - Model.Width * Scale / 2 + _pan.X, ActualHeight / 2 - Model.Height * Scale / 2 + _pan.Y);

    private PointD ToPicture(Point p)
    {
        var o = Origin;
        double s = Scale;
        return new PointD((p.X - o.X) / s, (p.Y - o.Y) / s);
    }

    public Cursor ToolCursor()
    {
        if (Panning || _panFrom is not null) return Cursors.Hand;
        return Model.Tool switch
        {
            EditorTool.Select => Cursors.Arrow,
            EditorTool.Text => Cursors.IBeam,
            _ => Cursors.Cross,
        };
    }

    // MARK: Zoom

    public void ZoomBy(double factor) => SetZoom(_zoom * factor);

    public void ResetZoom()
    {
        _pan = default;
        SetZoom(1);
    }

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, 0.25, 8);
        if (Math.Abs(_zoom - 1) < 0.001)
        {
            _zoom = 1;
            _pan = default;
        }
        Changed();
    }

    private void Changed()
    {
        _window.SetZoomBadge(_zoom);
        _window.SyncTextBox();
        InvalidateVisual();
    }

    // MARK: Drawing

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double s = Scale;
        var o = Origin;
        var shown = new Rect(o.X, o.Y, Model.Width * s, Model.Height * s);
        dc.DrawRoundedRectangle(Palette.Fade(Brushes.Black, 0.35), null, new Rect(shown.X - 2, shown.Y + 6, shown.Width + 4, shown.Height + 4), 8, 8);
        dc.PushClip(new RectangleGeometry(shown, 6, 6));
        dc.PushTransform(new MatrixTransform(s, 0, 0, s, o.X, o.Y));
        _window.Renderer.Draw(dc, s, exporting: false);
        dc.Pop();
        dc.Pop();
        dc.DrawRoundedRectangle(null, Edge, shown, 6, 6);

        if (Model.Tool == EditorTool.Picker && _hover is { } hover && PickerColor(hover) is { } color)
            DrawReadout(dc, hover, color);
    }

    private uint? PickerColor(Point screen)
    {
        var p = ToPicture(screen);
        if (p.X < 0 || p.Y < 0 || p.X >= Model.Width || p.Y >= Model.Height) return null;
        return _window.PixelAt(p);
    }

    /// <summary>The color under the pointer and its hex code, beside the pointer.</summary>
    private static void DrawReadout(DrawingContext dc, Point at, uint color)
    {
        var text = AnnotationRenderer.Text(EditorTools.Hex(color), 11, FontWeights.SemiBold, Brushes.White);
        var pill = new Rect(at.X + 16, at.Y + 16, text.Width + 16 + 20, text.Height + 10);
        dc.DrawRoundedRectangle(Palette.Fade(Brushes.Black, 0.78), null, pill, pill.Height / 2, pill.Height / 2);
        dc.DrawRoundedRectangle(AnnotationRenderer.ToBrush(color), new Pen(Palette.Fade(Brushes.White, 0.5), 0.5), new Rect(pill.X + 8, pill.Y + (pill.Height - 14) / 2, 14, 14), 3, 3);
        dc.DrawText(text, new Point(pill.X + 28, pill.Y + 5));
    }

    // MARK: Mouse

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        // One gesture at a time: a press during a middle-button pan is ignored.
        if (_panFrom is not null) return;
        var p = e.GetPosition(this);
        if (Panning)
        {
            StartPan(p, MouseButton.Left);
        }
        else
        {
            // A click away from a text being typed places it, unless the text tool places the next one.
            if (Model.EditingText is not null && Model.Tool != EditorTool.Text) Model.CommitText();
            _dragStart = ToPicture(p);
            _clicks = e.ClickCount;
            Model.DragChanged(_dragStart.Value, _dragStart.Value, Shift);
        }
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        // The middle button pans too.
        if (e.ChangedButton == MouseButton.Middle && _dragStart is null && _panFrom is null)
        {
            StartPan(e.GetPosition(this), MouseButton.Middle);
            CaptureMouse();
            e.Handled = true;
        }
        base.OnMouseDown(e);
    }

    private void StartPan(Point p, MouseButton button)
    {
        _panFrom = p;
        _panButton = button;
        _panStart = _pan;
        Cursor = ToolCursor();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_panFrom is { } from)
        {
            _pan = _panStart + (p - from);
            Changed();
            return;
        }
        if (_dragStart is { } start)
        {
            Model.DragChanged(start, ToPicture(p), Shift);
            return;
        }
        if (Model.Tool == EditorTool.Picker)
        {
            _hover = p;
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_panFrom is not null && e.ChangedButton == _panButton)
        {
            _panFrom = null;
            ReleaseMouseCapture();
            Cursor = ToolCursor();
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Left && _dragStart is { } start)
        {
            _dragStart = null;
            ReleaseMouseCapture();
            Model.DragEnded(ToPicture(e.GetPosition(this)), start, _clicks);
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        // Another window took the mouse mid-drag: finish where it was, as a release would.
        if (_dragStart is { } start)
        {
            _dragStart = null;
            Model.DragEnded(ToPicture(Mouse.GetPosition(this)), start, _clicks);
        }
        _panFrom = null;
        base.OnLostMouseCapture(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = null;
        if (Model.Tool == EditorTool.Picker) InvalidateVisual();
    }

    /// <summary>Ctrl+wheel (and a touchpad pinch) zooms; the wheel moves a zoomed-in picture.</summary>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double notches = e.Delta / 120.0;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) ZoomBy(Math.Pow(1.1, notches));
        else if (_zoom > 1)
        {
            if (Shift) _pan.X += notches * 40;
            else _pan.Y += notches * 40;
            Changed();
        }
        e.Handled = true;
    }

    private static bool Shift => (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
}
