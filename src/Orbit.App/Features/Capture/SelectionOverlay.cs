using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orbit.App.Design;
using Orbit.App.Overlays;
using Orbit.Core.Capture;
using Orbit.Core.Display;
using Orbit.Core.Launcher;

namespace Orbit.App.Features.Capture;

/// <summary>What happens to a capture instead of the usual steps, picked with a key while selecting (Orbit: <c>CaptureAction</c>).</summary>
internal enum CaptureKeyAction { Copy, Save, Text, Pin, Edit }

/// <summary>
/// The selection: a frozen monitor and an area on it (virtual-screen pixels), or a window to
/// capture on its own, and the key-picked action.
/// </summary>
internal sealed record SelectionResult(MonitorInfo Monitor, ScreenFrame Frame, PixelRect Area, OpenWindow? Window, CaptureKeyAction? Action);

/// <summary>
/// Selecting over frozen monitors (Orbit: SelectionOverlay.swift), one overlay per monitor. Drag to
/// select an area, Space toggles window mode, Enter takes the whole monitor, Esc cancels. While
/// dragging: Shift locks a square, Alt grows from the center, Space moves, arrows nudge. Before:
/// Tab cycles ratios, R brings back the last area, Ctrl+C copies the color under the loupe. C S T P E
/// pick what happens to the result (Orbit's V and L are not ported). Recording picks its area here too.
/// </summary>
internal sealed class SelectionSession
{
    /// <summary>Tab's choice, kept for the next capture while Orbit runs.</summary>
    private static AspectPreset _lastPreset = AspectPreset.Free;

    private readonly List<SelectionOverlay> _overlays = [];
    // Asynchronous continuations: whoever awaits the result runs after Finish has closed every overlay.
    private readonly TaskCompletionSource<SelectionResult?> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private SelectionSession(IReadOnlyList<OpenWindow> targets, bool windowMode, bool textMode, PixelRect? lastArea, bool recording)
    {
        Targets = targets;
        WindowMode = windowMode && targets.Count > 0;
        TextMode = textMode;
        Recording = recording;
        LastArea = lastArea;
        Preset = _lastPreset;
    }

    public IReadOnlyList<OpenWindow> Targets { get; }
    public bool WindowMode { get; private set; }
    public bool TextMode { get; }
    /// <summary>Picking what to record: the C S T P E actions are off and the hints say so.</summary>
    public bool Recording { get; }
    public bool AllowsActions => !TextMode && !Recording;
    public PixelRect? LastArea { get; }
    public AspectPreset Preset { get; private set; }
    public CaptureKeyAction? Action { get; private set; }
    public SelectionOverlay? Active { get; private set; }
    public bool KeyHints { get; init; } = true;

    /// <param name="frames">One frozen frame per monitor, in the same order.</param>
    public static Task<SelectionResult?> RunAsync(IReadOnlyList<MonitorInfo> monitors, IReadOnlyList<ScreenFrame> frames,
        IReadOnlyList<OpenWindow> targets, bool windowMode, bool textMode, PixelRect? lastArea, bool keyHints, bool recording = false)
    {
        var session = new SelectionSession(targets, windowMode, textMode, lastArea, recording) { KeyHints = keyHints };
        try
        {
            for (int i = 0; i < monitors.Count; i++)
            {
                var overlay = new SelectionOverlay(session, monitors[i], frames[i]);
                // Closed some other way (Alt+F4): the whole selection ends.
                overlay.Closed += (_, _) => session.Finish(null);
                session._overlays.Add(overlay);
                overlay.ShowAt(monitors[i].Bounds);
            }
            var (x, y) = Monitors.Cursor();
            var active = session._overlays.FirstOrDefault(o => o.Monitor.Bounds.Contains(x, y)) ?? session._overlays[0];
            active.Activate();
            session.SetActive(active);
        }
        catch
        {
            // No overlay may stay up over a screen that can't be used.
            session.Finish(null);
            throw;
        }
        return session._done.Task;
    }

    /// <summary>The monitor being worked on shows the key hints; the others don't.</summary>
    public void SetActive(SelectionOverlay overlay)
    {
        if (Active == overlay) return;
        Active = overlay;
        foreach (var o in _overlays.Where(o => o != overlay)) o.HideHints();
        overlay.PokeHints();
    }

    public void ToggleWindowMode()
    {
        if (TextMode || Targets.Count == 0) return;
        WindowMode = !WindowMode;
        RedrawAll();
    }

    public void CyclePreset(bool backward)
    {
        Preset = _lastPreset = Preset.Next(backward);
        RedrawAll();
    }

    /// <summary>Pressing the picked key again goes back to the usual steps.</summary>
    public void ToggleAction(CaptureKeyAction action)
    {
        if (!AllowsActions) return;
        Action = Action == action ? null : action;
        RedrawAll();
    }

    /// <summary>R: the last captured area, on its monitor. Enter captures it, Esc goes back.</summary>
    public void RestoreLast()
    {
        if (LastArea is not { } last || _overlays.FirstOrDefault(o => o.Monitor.Bounds.Contains(last.X, last.Y)) is not { } overlay)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }
        WindowMode = false;
        foreach (var o in _overlays) o.ClearRestore();
        overlay.Restore(last.RelativeTo(overlay.Monitor.Bounds));
        overlay.Activate();
        SetActive(overlay);
        RedrawAll();
    }

    /// <summary>Enter: the brought-back or half-dragged area if there is one, else the whole monitor.</summary>
    public void Confirm(SelectionOverlay from)
    {
        if (_overlays.FirstOrDefault(o => o.PendingArea is not null) is { } pending) Complete(pending, pending.PendingArea!.Value);
        else Complete(from, new Area(0, 0, from.Monitor.Bounds.Width, from.Monitor.Bounds.Height));
    }

    /// <summary>An area on one monitor, in its local pixels.</summary>
    public void Complete(SelectionOverlay overlay, Area local)
    {
        var bounds = overlay.Monitor.Bounds;
        int left = (int)Math.Round(local.X), top = (int)Math.Round(local.Y);
        int right = (int)Math.Round(local.Right), bottom = (int)Math.Round(local.Bottom);
        var area = new PixelRect(bounds.X + left, bounds.Y + top, right - left, bottom - top).Intersect(bounds);
        Finish(area.IsEmpty ? null : new SelectionResult(overlay.Monitor, overlay.Frame, area, null, Action));
    }

    public void Complete(SelectionOverlay overlay, OpenWindow window) =>
        Finish(new SelectionResult(overlay.Monitor, overlay.Frame, window.Bounds.Intersect(overlay.Monitor.Bounds), window, Action));

    /// <summary>The first call decides the result; closing the overlays then calls back here and changes nothing.</summary>
    public void Finish(SelectionResult? result)
    {
        if (!_done.TrySetResult(result)) return;
        var overlays = _overlays.ToArray();
        _overlays.Clear();
        foreach (var overlay in overlays)
        {
            try
            {
                overlay.Close();
            }
            catch (InvalidOperationException e)
            {
                Orbit.Core.Log.Error("A capture overlay could not be closed", e);
            }
        }
    }

    private void RedrawAll()
    {
        foreach (var overlay in _overlays) overlay.Redraw();
    }
}

/// <summary>One monitor's overlay: the frozen frame pixel for pixel, dimmed outside the selection.</summary>
internal sealed class SelectionOverlay : OverlayWindow
{
    private const double Loupe = 116, LoupeCells = 15;
    private static readonly Brush Dim = Controls.Frozen(new SolidColorBrush(Color.FromArgb(92, 0, 0, 0)));
    private static readonly Brush DimText = Controls.Frozen(new SolidColorBrush(Color.FromArgb(107, 0, 0, 0)));
    private static readonly Brush PillFill = Controls.Frozen(new SolidColorBrush(Color.FromArgb(184, 0, 0, 0)));
    private static readonly Brush PillLight = Controls.Frozen(new SolidColorBrush(Color.FromArgb(242, 255, 255, 255)));
    private static readonly Pen PillEdge = Controls.Frozen(new Pen(new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)), 0.5));
    private static readonly Pen Outline = Controls.Frozen(new Pen(new SolidColorBrush(Color.FromArgb(242, 255, 255, 255)), 1));
    private static readonly Pen Crosshair = Controls.Frozen(new Pen(new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), 0.5) { DashStyle = new DashStyle([8, 6], 0) });
    private static readonly Pen LoupeGrid = Controls.Frozen(new Pen(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), 0.5));
    private static readonly Pen LoupeCenter = Controls.Frozen(new Pen(Brushes.White, 1.5));
    private static readonly Pen LoupeEdge = Controls.Frozen(new Pen(new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)), 1));
    private static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI, Malgun Gothic"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    private readonly SelectionSession _session;
    private readonly BitmapSource _bitmap;
    private readonly Surface _surface;
    private readonly Border _hints;
    private readonly DispatcherTimer _hintFade = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Brush _accent;
    private readonly Pen _windowEdge;
    private readonly Brush _windowFill;
    private (double X, double Y)? _mouse;
    private SelectionGeometry? _geometry;
    private Area? _selection;
    private OpenWindow? _hovered;
    private bool _restored;
    private Area? _restoredAtDown;
    private bool _carrying;
    private string _hintKey = "";

    public SelectionOverlay(SelectionSession session, MonitorInfo monitor, ScreenFrame frame) : base(activating: true, transparent: false, clickThrough: false)
    {
        _session = session;
        Monitor = monitor;
        Frame = frame;
        _bitmap = FrameImages.ToBitmap(frame, monitor.Dpi);
        _accent = Palette.DarkTheme.Accent;
        _windowEdge = Controls.Frozen(new Pen(_accent, 3));
        _windowFill = Controls.Frozen(new SolidColorBrush(Color.FromArgb(31, Palette.DarkTheme.Accent.Color.R, Palette.DarkTheme.Accent.Color.G, Palette.DarkTheme.Accent.Color.B)));
        Cursor = Cursors.Cross;

        _surface = new Surface(this);
        RenderOptions.SetBitmapScalingMode(_surface, BitmapScalingMode.NearestNeighbor);
        _hints = new Border
        {
            Background = PillFill, BorderBrush = PillEdge.Brush, BorderThickness = new Thickness(0.5), CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 28), Opacity = 0, IsHitTestVisible = false,
        };
        var root = new Grid();
        root.Children.Add(_surface);
        root.Children.Add(_hints);
        Content = root;

        _hintFade.Tick += (_, _) => HideHints();
        MouseEnter += (_, _) =>
        {
            Activate();
            _session.SetActive(this);
        };
        MouseLeave += (_, _) =>
        {
            if (IsMouseCaptured) return;
            _mouse = null;
            _surface.InvalidateVisual();
        };
        MouseMove += (_, _) => Moved();
        MouseLeftButtonDown += (_, _) => Pressed();
        MouseLeftButtonUp += (_, _) => Released();
        PreviewKeyDown += KeyPressed;
        PreviewKeyUp += KeyReleased;
        Closed += (_, _) => _hintFade.Stop();
        RefreshHints();
    }

    public MonitorInfo Monitor { get; }
    public ScreenFrame Frame { get; }

    private Area Bounds => new(0, 0, Monitor.Bounds.Width, Monitor.Bounds.Height);
    private bool Dragging => _geometry is not null;

    /// <summary>What Enter takes from this monitor, if anything.</summary>
    public Area? PendingArea => _selection is { Width: >= 4, Height: >= 4 } s && (_restored || Dragging) ? s : null;

    private static bool Shift => (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
    private static bool Alt => (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

    /// <summary>The drag's ratio: Tab's preset, or a square while Shift is held.</summary>
    private double? Aspect => _session.Preset.Ratio() ?? (Shift ? 1 : null);

    // MARK: Input

    /// <summary>The pointer in this monitor's pixels, read from Windows, so no DIP rounding gets into the area.</summary>
    private (double X, double Y) Pointer()
    {
        var (x, y) = Monitors.Cursor();
        return (x - Monitor.Bounds.X, y - Monitor.Bounds.Y);
    }

    private void Moved()
    {
        var p = Pointer();
        _mouse = p;
        if (_geometry is { } g)
        {
            g.Move(p.X, p.Y, _carrying, Aspect, Alt, Bounds);
            UpdateSelection();
        }
        else if (_session.WindowMode)
        {
            _hovered = WindowAt(p);
        }
        _surface.InvalidateVisual();
    }

    private void Pressed()
    {
        var p = Pointer();
        _restoredAtDown = _restored ? _selection : null;
        _restored = false;
        _geometry = new SelectionGeometry(p.X, p.Y);
        _selection = null;
        CaptureMouse();
        RefreshHints();
    }

    private void Released()
    {
        if (_geometry is null) return; // Esc called the drag off.
        var selection = _selection;
        var restored = _restoredAtDown;
        EndDrag();
        if (selection is { Width: >= 4, Height: >= 4 } area)
        {
            _session.Complete(this, area);
            return;
        }
        var p = Pointer();
        // A click inside the brought-back area takes it.
        if (restored is { } last && last.Contains(p.X, p.Y))
        {
            _session.Complete(this, last);
            return;
        }
        // A click without a drag captures the window under it.
        if (!_session.TextMode && (_session.WindowMode ? _hovered : WindowAt(p)) is { } target)
        {
            _session.Complete(this, target);
            return;
        }
        _selection = null;
        _surface.InvalidateVisual();
    }

    private void EndDrag()
    {
        _geometry = null;
        _carrying = false;
        _restoredAtDown = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        RefreshHints();
    }

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        // Tab must not move focus and Alt must not open a menu: every key is the overlay's.
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        PokeHints();
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        switch (key)
        {
            case Key.Escape:
                if (Dragging && _selection is not null)
                {
                    _selection = null;
                    EndDrag();
                    Redraw();
                }
                else if (_restored)
                {
                    ClearRestore();
                    Redraw();
                }
                else
                {
                    _session.Finish(null);
                }
                break;
            case Key.Space:
                if (e.IsRepeat) return;
                if (Dragging) _carrying = true;
                else
                {
                    _session.ToggleWindowMode();
                    _hovered = _session.WindowMode && _mouse is { } m ? WindowAt(m) : null;
                }
                break;
            case Key.Enter:
                _session.Confirm(this);
                break;
            case Key.Tab:
                _session.CyclePreset(backward: Shift);
                break;
            case Key.R when !Dragging && !ctrl:
                if (!e.IsRepeat) _session.RestoreLast();
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down:
                double step = Shift ? 10 : 1;
                var (dx, dy) = key switch { Key.Left => (-step, 0.0), Key.Right => (step, 0.0), Key.Up => (0.0, -step), _ => (0.0, step) };
                if (_geometry is { } g)
                {
                    g.Nudge(dx, dy, Bounds);
                    UpdateSelection();
                }
                else if (_restored && _selection is { } s)
                {
                    _selection = SelectionGeometry.Shifted(s, dx, dy, Bounds);
                }
                break;
            case Key.C when ctrl:
                CopyColor();
                break;
            case Key.C or Key.S or Key.T or Key.P or Key.E when !ctrl:
                if (!e.IsRepeat)
                    _session.ToggleAction(key switch
                    {
                        Key.C => CaptureKeyAction.Copy, Key.S => CaptureKeyAction.Save, Key.P => CaptureKeyAction.Pin, Key.E => CaptureKeyAction.Edit,
                        _ => CaptureKeyAction.Text,
                    });
                break;
            case Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt:
                UpdateSelection();
                break;
        }
        _surface.InvalidateVisual();
    }

    private void KeyReleased(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Space) _carrying = false;
        if (key is Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt)
        {
            UpdateSelection();
            _surface.InvalidateVisual();
        }
    }

    private void UpdateSelection()
    {
        if (_geometry is { } g) _selection = g.Rect(Aspect, Alt, Bounds);
    }

    public void Restore(PixelRect local)
    {
        _geometry = null;
        _selection = new Area(local.X, local.Y, local.Width, local.Height);
        _restored = true;
        Redraw();
    }

    public void ClearRestore()
    {
        if (!_restored) return;
        _restored = false;
        _selection = null;
        _surface.InvalidateVisual();
    }

    /// <summary>Preset, action, mode or phase changed.</summary>
    public void Redraw()
    {
        UpdateSelection();
        if (!_session.WindowMode) _hovered = null;
        else if (_mouse is { } m) _hovered = WindowAt(m);
        RefreshHints();
        _surface.InvalidateVisual();
    }

    /// <summary>The frontmost window at a point of this monitor.</summary>
    private OpenWindow? WindowAt((double X, double Y) local)
    {
        int x = (int)local.X + Monitor.Bounds.X, y = (int)local.Y + Monitor.Bounds.Y;
        return _session.Targets.FirstOrDefault(w => w.Bounds.Contains(x, y));
    }

    // MARK: Color

    /// <summary>Where the loupe looks: the moving corner while dragging, else the pointer.</summary>
    private (double X, double Y)? LoupePoint
    {
        get
        {
            if (_geometry is { } g)
            {
                var (tx, ty) = g.Tip;
                return (Math.Clamp(tx, 0, Monitor.Bounds.Width - 1), Math.Clamp(ty, 0, Monitor.Bounds.Height - 1));
            }
            return _mouse;
        }
    }

    private void CopyColor()
    {
        if (LoupePoint is not { } p) return;
        int x = Math.Clamp((int)p.X, 0, Frame.Width - 1), y = Math.Clamp((int)p.Y, 0, Frame.Height - 1);
        int i = y * Frame.Stride + x * 4;
        var hex = SelectionGeometry.Hex(Frame.Pixels[i + 2], Frame.Pixels[i + 1], Frame.Pixels[i]);
        Clipboards.CopyText(hex, Strings.ColorCopied(hex));
    }

    // MARK: Key hints

    private IReadOnlyList<(string Keys, string Title)> CurrentHints()
    {
        (string, string)[] actions = _session.AllowsActions ? [("C · S · T · P · E", Strings.KeyAction)] : [];
        if (_restored)
            return [("Enter", Strings.KeyCaptureIt), ("Esc", Strings.KeyPickAgain), ("←↑↓→", Strings.KeyMoveSelection), .. actions];
        if (Dragging)
            return [("Shift", Strings.KeyLockRatio), ("Alt", Strings.KeyFromCenter), ("Space", Strings.KeyMoveSelection), ("←↑↓→", Strings.KeyNudge), .. actions];
        if (_session.WindowMode)
            return [("Space", Strings.KeySelectArea), .. actions];
        return [("Shift", Strings.KeyLockRatio), ("Alt", Strings.KeyFromCenter), ("Tab", Strings.KeyRatio), ("R", Strings.KeyLastArea), ("Ctrl C", Strings.KeyCopyColor), .. actions];
    }

    /// <summary>Updates what the strip says, without showing it.</summary>
    private void RefreshHints()
    {
        if (!_session.KeyHints) return;
        var items = CurrentHints();
        var key = string.Join("|", items.Select(i => i.Keys + i.Title));
        if (key == _hintKey) return;
        _hintKey = key;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (keys, title) in items)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(row.Children.Count == 0 ? 0 : 14, 0, 0, 0) };
            item.Children.Add(KeyCap.Row(keys.Split(' '), Palette.DarkTheme));
            item.Children.Add(new TextBlock { Text = title, FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(209, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
            row.Children.Add(item);
        }
        _hints.Child = row;
    }

    /// <summary>A key was pressed: show the strip, then fade it after a few quiet seconds.</summary>
    public void PokeHints()
    {
        if (!_session.KeyHints || _session.Active != this) return;
        RefreshHints();
        _hintFade.Stop();
        _hintFade.Start();
        if (_hints.Opacity < 1) _hints.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
    }

    public void HideHints()
    {
        _hintFade.Stop();
        if (_hints.Opacity > 0) _hints.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
    }

    // MARK: Drawing

    private double Dip(double pixels) => pixels / Monitor.Scale;

    private Rect Dip(Area a) => new(Dip(a.X), Dip(a.Y), Dip(a.Width), Dip(a.Height));

    private Rect Local(PixelRect global) => Dip(new Area(global.X - Monitor.Bounds.X, global.Y - Monitor.Bounds.Y, global.Width, global.Height));

    private void Render(DrawingContext dc)
    {
        var full = new Rect(0, 0, Dip(Monitor.Bounds.Width), Dip(Monitor.Bounds.Height));
        dc.DrawImage(_bitmap, full);

        Rect? hole = _selection is { } s ? Dip(s) : _session.WindowMode && _hovered is { } w ? Local(w.Bounds) : null;
        Geometry dim = new RectangleGeometry(full);
        if (hole is { } h) dim = new CombinedGeometry(GeometryCombineMode.Exclude, dim, new RectangleGeometry(h));
        dc.DrawGeometry(_session.TextMode ? DimText : Dim, null, dim);

        if (_selection is { } selection) DrawSelection(dc, selection);
        else if (_session.WindowMode && _hovered is { } target) DrawWindow(dc, target);
        else if (_mouse is { } m) DrawCrosshair(dc, new Point(Dip(m.X), Dip(m.Y)), full);

        if (_mouse is not null && LoupePoint is { } p && (!Dragging || _selection is not null) && !_session.WindowMode) DrawLoupe(dc, p, full);
        if (_mouse is not null && _selection is null)
        {
            var hint = _session.TextMode ? Strings.CaptureHintText
                : _session.Recording ? (_session.WindowMode ? Strings.RecordHintWindow : Strings.RecordHintArea)
                : _session.WindowMode ? Strings.CaptureHintWindow : Strings.CaptureHintArea;
            DrawPills(dc, [new Pill(hint)], new Point(full.Width / 2, 72), large: true);
        }
    }

    private void DrawSelection(DrawingContext dc, Area area)
    {
        var rect = Dip(area);
        dc.DrawRectangle(null, Outline, new Rect(rect.X - 0.5, rect.Y - 0.5, rect.Width + 1, rect.Height + 1));
        foreach (var corner in new[] { rect.TopLeft, rect.TopRight, rect.BottomLeft, rect.BottomRight })
            dc.DrawEllipse(Brushes.White, null, corner, 3, 3);
        var label = $"{(int)Math.Round(area.Width)} × {(int)Math.Round(area.Height)}";
        bool below = rect.Bottom + 38 < Dip(Monitor.Bounds.Height);
        DrawPills(dc, [new Pill(label), .. Chips(dragging: true)], new Point(rect.X + rect.Width / 2, below ? rect.Bottom + 10 : rect.Y - 30));
    }

    private void DrawWindow(DrawingContext dc, OpenWindow target)
    {
        var rect = Local(target.Bounds);
        dc.DrawRectangle(_windowFill, null, rect);
        rect.Inflate(-1.5, -1.5);
        dc.DrawRectangle(null, _windowEdge, rect);
        var name = target.Title.Length == 0 ? target.AppName : target.AppName.Length == 0 ? target.Title : $"{target.AppName} — {target.Title}";
        if (name.Length > 60) name = name[..60];
        var pills = new List<Pill> { new(name) };
        if (_session.Action is { } a) pills.Add(new Pill(ActionTitle(a), Filled: true));
        DrawPills(dc, pills, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2 - 11), large: true);
    }

    private static void DrawCrosshair(DrawingContext dc, Point p, Rect full)
    {
        dc.DrawLine(Crosshair, new Point(full.Left, p.Y), new Point(full.Right, p.Y));
        dc.DrawLine(Crosshair, new Point(p.X, full.Top), new Point(p.X, full.Bottom));
    }

    /// <summary>A pixel-precise magnifier, away from the pointer and kept on the monitor.</summary>
    private void DrawLoupe(DrawingContext dc, (double X, double Y) px, Rect full)
    {
        var p = new Point(Dip(px.X), Dip(px.Y));
        double x = p.X + 24, y = p.Y + 24;
        if (x + Loupe > full.Right) x = p.X - Loupe - 24;
        if (y + Loupe + 30 > full.Bottom) y = p.Y - Loupe - 24;
        var frame = new Rect(x, y, Loupe, Loupe);

        int half = (int)(LoupeCells / 2);
        int sx = (int)px.X - half, sy = (int)px.Y - half;
        var source = new Int32Rect(Math.Clamp(sx, 0, _bitmap.PixelWidth - (int)LoupeCells), Math.Clamp(sy, 0, _bitmap.PixelHeight - (int)LoupeCells), (int)LoupeCells, (int)LoupeCells);
        var clip = new RectangleGeometry(frame, 14, 14);
        dc.DrawGeometry(Brushes.Black, null, clip);
        dc.PushClip(clip);
        if (_bitmap.PixelWidth >= LoupeCells && _bitmap.PixelHeight >= LoupeCells)
            dc.DrawImage(new CroppedBitmap(_bitmap, source), frame);
        double cell = Loupe / LoupeCells;
        for (int i = 1; i < LoupeCells; i++)
        {
            dc.DrawLine(LoupeGrid, new Point(frame.X + i * cell, frame.Y), new Point(frame.X + i * cell, frame.Bottom));
            dc.DrawLine(LoupeGrid, new Point(frame.X, frame.Y + i * cell), new Point(frame.Right, frame.Y + i * cell));
        }
        // The pixel under the pointer, where the source wasn't pushed back from an edge.
        double cx = frame.X + (half + sx - source.X) * cell, cy = frame.Y + (half + sy - source.Y) * cell;
        dc.DrawRectangle(null, LoupeCenter, new Rect(cx, cy, cell, cell));
        dc.Pop();
        dc.DrawGeometry(null, LoupeEdge, clip);

        // Before a drag the chips ride here; once a selection exists they sit by its size.
        var pills = new List<Pill> { new($"{(int)px.X + Monitor.Bounds.X}, {(int)px.Y + Monitor.Bounds.Y}") };
        if (_selection is null) pills.AddRange(Chips(dragging: false));
        DrawPills(dc, pills, new Point(frame.X + Loupe / 2, frame.Bottom + 8));
    }

    /// <summary>The ratio and action chips beside the size label (or the loupe before a drag).</summary>
    private IEnumerable<Pill> Chips(bool dragging)
    {
        var ratio = _session.Preset != AspectPreset.Free ? _session.Preset : dragging && Shift ? AspectPreset.Square : (AspectPreset?)null;
        if (ratio is { } r) yield return new Pill(r.Label());
        if (_session.Action is { } a) yield return new Pill(ActionTitle(a), Filled: true);
    }

    private static string ActionTitle(CaptureKeyAction action) => action switch
    {
        CaptureKeyAction.Copy => Strings.CaptureActionCopy,
        CaptureKeyAction.Save => Strings.CaptureActionSave,
        CaptureKeyAction.Pin => Strings.CaptureActionPin,
        CaptureKeyAction.Edit => Strings.CaptureActionEdit,
        _ => Strings.CaptureActionText,
    };

    private readonly record struct Pill(string Text, bool Filled = false);

    /// <summary>Labels in a row centered on <paramref name="center"/>.X, their tops at <paramref name="center"/>.Y.</summary>
    private void DrawPills(DrawingContext dc, IReadOnlyList<Pill> pills, Point center, bool large = false)
    {
        double size = large ? 13 : 11, padX = large ? 14 : 8, padY = large ? 7 : 4, gap = 6;
        double dpi = Monitor.Scale;
        var texts = pills.Select(p => new FormattedText(p.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size,
            p.Filled ? Brushes.Black : Brushes.White, dpi)).ToList();
        double total = texts.Sum(t => t.Width + padX * 2) + gap * (texts.Count - 1);
        double x = center.X - total / 2;
        for (int i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            var rect = new Rect(x, center.Y, text.Width + padX * 2, text.Height + padY * 2);
            dc.DrawRoundedRectangle(pills[i].Filled ? PillLight : PillFill, pills[i].Filled ? null : PillEdge, rect, rect.Height / 2, rect.Height / 2);
            dc.DrawText(text, new Point(rect.X + padX, rect.Y + padY));
            x += rect.Width + gap;
        }
    }

    /// <summary>Draws the overlay; the window keeps the state.</summary>
    private sealed class Surface(SelectionOverlay owner) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc) => owner.Render(dc);
    }
}
