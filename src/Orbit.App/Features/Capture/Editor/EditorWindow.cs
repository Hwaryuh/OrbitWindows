using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbit.App.Design;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Capture;
using Orbit.Core.Capture.Editor;
using Orbit.Core.Display;
using Orbit.Core.Settings;
using Windows.Win32;
using Windows.Win32.Graphics.Dwm;

namespace Orbit.App.Features.Capture.Editor;

/// <summary>
/// The capture editor (Orbit: EditorWindow.swift): a toolbar of tools, colors and style over the
/// picture. Single-letter keys pick tools, 1–8 colors; Ctrl+Z undoes, Ctrl+C copies, Ctrl+S saves;
/// Ctrl+wheel zooms and Space-drag pans. Closing it brings the capture back as a kept card.
/// </summary>
internal sealed class EditorWindow : Window
{
    private static readonly HashSet<EditorWindow> OpenEditors = [];
    private static readonly Brush Backdrop = new SolidColorBrush(Color.FromRgb(33, 33, 33)).Frozen();
    private static readonly Brush ToolbarFill = new SolidColorBrush(Color.FromRgb(24, 24, 26)).Frozen();
    private static readonly Brush GlassFill = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)).Frozen();
    private static readonly Brush HoverFill = new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)).Frozen();
    private const double ToolSize = 32;

    private readonly CaptureItem _item;
    private readonly QuickAccess _cards;
    private readonly Preferences _preferences;
    private readonly EditorModel _model;
    private readonly ScreenFrame _frame;
    private readonly RedactionCache _redactions;
    private readonly AnnotationRenderer _renderer;
    private readonly EditorCanvas _canvas;
    private readonly TextBox _textBox;
    private readonly System.Windows.Controls.Canvas _textLayer;
    private readonly Button _zoomBadge;
    private readonly List<(ToolGroup Group, Button Button)> _groupButtons = [];
    private readonly List<(uint Color, Border Ring)> _swatches = [];
    private readonly StackPanel _swatchStrip;
    private readonly Button _colorButton;
    private readonly Button _undo, _redo, _resetCrop, _floating;
    private readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
    private bool _returnCard = true;

    private EditorWindow(CaptureItem item, QuickAccess cards, Preferences preferences)
    {
        _item = item;
        _cards = cards;
        _preferences = preferences;
        var image = item.Image;
        double scale = PixelScale(image);
        _frame = FrameImages.ToFrame(image);
        _model = new EditorModel(image.PixelWidth / scale, image.PixelHeight / scale, scale, AnnotationRenderer.Measure, PixelAt);
        _redactions = new RedactionCache(_frame, image.DpiX > 0 ? image.DpiX : 96);
        _renderer = new AnnotationRenderer(_model, image, _redactions);

        Title = Strings.EditorTitle;
        Background = Backdrop;
        MinWidth = 760;
        MinHeight = 420;
        Topmost = preferences.Get<bool>(Pref.CaptureEditorFloating);
        // Letter keys are tools here; the IME only runs in the text box.
        InputMethod.SetIsInputMethodEnabled(this, false);

        _canvas = new EditorCanvas(this);
        _textBox = new TextBox
        {
            AcceptsReturn = true, BorderThickness = new Thickness(1), BorderBrush = Palette.DarkTheme.Accent, Background = Brushes.Transparent,
            MinWidth = 80, MaxWidth = 520, TextWrapping = TextWrapping.NoWrap, Visibility = Visibility.Collapsed, Padding = new Thickness(0),
            CaretBrush = Brushes.White,
        };
        InputMethod.SetIsInputMethodEnabled(_textBox, true);
        _textBox.TextChanged += (_, _) =>
        {
            if (_model.EditingText is { } editing) editing.Text = _textBox.Text;
        };
        _textLayer = new System.Windows.Controls.Canvas { ClipToBounds = true };
        _textLayer.Children.Add(_textBox);
        _zoomBadge = PillButton("100%", Strings.EditorFitHint, () => _canvas.ResetZoom());
        _zoomBadge.HorizontalAlignment = HorizontalAlignment.Right;
        _zoomBadge.VerticalAlignment = VerticalAlignment.Bottom;
        _zoomBadge.Margin = new Thickness(14);
        _zoomBadge.Visibility = Visibility.Collapsed;

        var stage = new Grid { ClipToBounds = true, Background = Backdrop };
        stage.Children.Add(_canvas);
        stage.Children.Add(_textLayer);
        stage.Children.Add(_zoomBadge);

        _swatchStrip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < EditorTools.Palette.Length; i++) _swatchStrip.Children.Add(Swatch(EditorTools.Palette[i], i + 1, null));
        _colorButton = new Button
        {
            Width = 34, Height = 34, Background = GlassFill, BorderThickness = new Thickness(0), Template = Controls.RoundTemplate(17),
            Cursor = Cursors.Hand, Focusable = false, Visibility = Visibility.Collapsed, ToolTip = Strings.EditorColor(1),
        };
        _colorButton.Click += (_, _) => ShowColors(_colorButton);
        _undo = IconButton("", Strings.EditorUndo, _model.Undo);
        _redo = IconButton("", Strings.EditorRedo, _model.Redo);
        _resetCrop = PillButton(Strings.EditorResetCrop, Strings.EditorResetCrop, _model.ResetCrop);
        _resetCrop.Margin = new Thickness(0, 0, 8, 0);
        _floating = IconButton("", Strings.EditorAlwaysOnTop, ToggleFloating);

        var root = new DockPanel();
        var toolbar = Toolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        root.Children.Add(stage);
        Content = root;

        _model.Changed += Refresh;
        _model.ColorPicked += hex => Clipboards.CopyText(hex, Strings.ColorCopied(hex));
        _redactions.Ready += _canvas.InvalidateVisual;
        PreviewKeyDown += KeyPressed;
        PreviewKeyUp += KeyReleased;
        SizeChanged += (_, _) => FitToolbar();
        SourceInitialized += (_, _) => TitleBars.MakeDark(this);
        Closed += (_, _) =>
        {
            OpenEditors.Remove(this);
            _redactions.Close();
            if (_returnCard) _cards.Add(_item, keep: true);
        };
        Refresh();
    }

    /// <summary>Opens the editor on a capture, centered on the monitor under the pointer.</summary>
    public static void Open(CaptureItem item, QuickAccess cards, Preferences preferences)
    {
        var window = new EditorWindow(item, cards, preferences);
        OpenEditors.Add(window);
        if (WindowPlacement.PointerMonitor() is { } monitor) WindowPlacement.ShowAt(window, Placement(window._model.Width, window._model.Height, monitor));
        else window.Show();
        window.Activate();
    }

    /// <summary>Orbit's size: the picture at up to 85 % of the work area, plus room for the toolbar, at least 820 × 480.</summary>
    private static PixelRect Placement(double pictureWidth, double pictureHeight, MonitorInfo monitor)
    {
        double workW = monitor.WorkArea.Width / monitor.Scale, workH = monitor.WorkArea.Height / monitor.Scale;
        const double chromeW = 80, chromeH = 140;
        double fit = Math.Min(1, Math.Min((workW * 0.85 - chromeW) / pictureWidth, (workH * 0.85 - chromeH) / pictureHeight));
        return WindowPlacement.Centered(monitor, Math.Max(820, pictureWidth * fit + chromeW), Math.Max(480, pictureHeight * fit + chromeH));
    }


    public EditorModel Model => _model;
    public AnnotationRenderer Renderer => _renderer;

    /// <summary>Picture pixels per point: the capture's monitor scale, kept in the picture's DPI.</summary>
    private static double PixelScale(BitmapSource image) => image.DpiX > 0 ? image.DpiX / 96 : 1;

    /// <summary>The picture's color at a point, for the picker and the eraser.</summary>
    public uint? PixelAt(PointD p)
    {
        double scale = PixelScale(_item.Image);
        int x = (int)Math.Floor(p.X * scale), y = (int)Math.Floor(p.Y * scale);
        if (x < 0 || y < 0 || x >= _frame.Width || y >= _frame.Height) return null;
        int i = y * _frame.Stride + x * 4;
        return 0xFF000000 | (uint)_frame.Pixels[i + 2] << 16 | (uint)_frame.Pixels[i + 1] << 8 | _frame.Pixels[i];
    }

    // MARK: State to screen

    private void Refresh()
    {
        foreach (var (group, button) in _groupButtons)
        {
            bool active = group.Tools().Contains(_model.Tool);
            button.Background = active ? Brushes.White : Brushes.Transparent;
            if (button.Content is Grid grid && grid.Children[0] is TextBlock glyph)
            {
                glyph.Text = ToolGlyph(_model.LastTool(group));
                glyph.Foreground = active ? Brushes.Black : Brushes.White;
                if (grid.Children.Count > 1 && grid.Children[1] is TextBlock chevron) chevron.Foreground = active ? Palette.Fade(Brushes.Black, 0.55) : Palette.Fade(Brushes.White, 0.55);
            }
            button.ToolTip = $"{Strings.EditorToolTitle(_model.LastTool(group))} · {_model.LastTool(group).Key()}";
        }
        foreach (var (color, ring) in _swatches) ring.BorderBrush = color == _model.Color ? Brushes.White : Brushes.Transparent;
        _colorButton.Content = Dot(_model.Color, 16);
        _undo.IsEnabled = _model.CanUndo;
        _undo.Opacity = _model.CanUndo ? 1 : 0.35;
        _redo.IsEnabled = _model.CanRedo;
        _redo.Opacity = _model.CanRedo ? 1 : 0.35;
        _resetCrop.Visibility = _model.Crop is null ? Visibility.Collapsed : Visibility.Visible;
        _floating.Foreground = Topmost ? Palette.DarkTheme.Accent : Palette.Fade(Brushes.White, 0.55);
        _floating.ToolTip = Topmost ? Strings.EditorAlwaysOnTop : Strings.EditorAlwaysOnTopOff;
        _canvas.Cursor = _canvas.ToolCursor();
        _canvas.InvalidateVisual();
        SyncTextBox();
    }

    /// <summary>The text box over the picture while a text is typed, matching how it will look.</summary>
    public void SyncTextBox()
    {
        if (_model.EditingText is not { } editing)
        {
            if (_textBox.Visibility == Visibility.Visible)
            {
                _textBox.Visibility = Visibility.Collapsed;
                _canvas.Focus();
            }
            return;
        }
        double scale = _canvas.Scale;
        bool pill = _model.TextStyle == TextStyle.Pill;
        double pad = pill ? _model.FontSize * 0.4 * scale : 2;
        _textBox.FontSize = Math.Max(1, _model.FontSize * scale);
        _textBox.FontWeight = AnnotationRenderer.Weight(_model.Weight);
        _textBox.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI, Malgun Gothic");
        _textBox.Foreground = AnnotationRenderer.ToBrush(pill ? EditorTools.Contrast(_model.Color) : _model.Color);
        _textBox.Background = pill ? AnnotationRenderer.ToBrush(_model.Color) : Brushes.Transparent;
        _textBox.Padding = new Thickness(pad, 0, pad, 0);
        var origin = _canvas.Origin;
        System.Windows.Controls.Canvas.SetLeft(_textBox, origin.X + editing.At.X * scale - pad - 1);
        System.Windows.Controls.Canvas.SetTop(_textBox, origin.Y + editing.At.Y * scale - 1);
        if (_textBox.Visibility != Visibility.Visible)
        {
            _textBox.Text = editing.Text;
            _textBox.Visibility = Visibility.Visible;
            _textBox.CaretIndex = _textBox.Text.Length;
            Dispatcher.BeginInvoke(() => _textBox.Focus());
        }
    }

    public void SetZoomBadge(double zoom)
    {
        _zoomBadge.Visibility = Math.Abs(zoom - 1) < 0.001 ? Visibility.Collapsed : Visibility.Visible;
        if (_zoomBadge.Content is TextBlock text) text.Text = $"{Math.Round(zoom * 100)}%";
    }

    // MARK: Keys

    private void KeyPressed(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0, shift = (mods & ModifierKeys.Shift) != 0;

        if (_textBox.IsKeyboardFocusWithin)
        {
            // A word still being composed by the IME belongs to the IME.
            if (key == Key.ImeProcessed) return;
            if (key == Key.Escape || (key == Key.Enter && mods == ModifierKeys.None))
            {
                _model.CommitText();
                e.Handled = true;
            }
            else if (ctrl && key == Key.S)
            {
                _model.CommitText();
                Save(ask: shift);
                e.Handled = true;
            }
            return;
        }
        if (key == Key.ImeProcessed) key = e.ImeProcessedKey;
        if (_popup.IsOpen && key == Key.Escape)
        {
            _popup.IsOpen = false;
            e.Handled = true;
            return;
        }

        e.Handled = true;
        if (ctrl)
        {
            switch (key)
            {
                case Key.Z when shift:
                case Key.Y:
                    _model.Redo();
                    return;
                case Key.Z:
                    _model.Undo();
                    return;
                case Key.C:
                    Copy();
                    return;
                case Key.S:
                    Save(ask: shift);
                    return;
                case Key.OemPlus or Key.Add:
                    _canvas.ZoomBy(1.25);
                    return;
                case Key.OemMinus or Key.Subtract:
                    _canvas.ZoomBy(1 / 1.25);
                    return;
                case Key.D0 or Key.NumPad0:
                    _canvas.ResetZoom();
                    return;
            }
            e.Handled = false;
            return;
        }
        switch (key)
        {
            case Key.Delete or Key.Back:
                _model.DeleteSelected();
                return;
            case Key.Escape:
                if (_model.SelectedId is not null) _model.SelectedId = null;
                else Close();
                return;
            case Key.Space:
                if (!e.IsRepeat) _canvas.Panning = true;
                return;
        }
        if ((mods & ModifierKeys.Alt) == 0)
        {
            if (key is >= Key.A and <= Key.Z && EditorTools.ForKey((char)('A' + (key - Key.A))) is { } tool)
            {
                _model.Tool = tool;
                return;
            }
            int digit = key is >= Key.D1 and <= Key.D8 ? key - Key.D1 : key is >= Key.NumPad1 and <= Key.NumPad8 ? key - Key.NumPad1 : -1;
            if (digit >= 0)
            {
                _model.Color = EditorTools.Palette[digit];
                return;
            }
        }
        e.Handled = false;
    }

    private void KeyReleased(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !_textBox.IsKeyboardFocusWithin) _canvas.Panning = false;
    }

    // MARK: Output

    /// <summary>The picture with its marks, cropped, at its own pixels (the backdrop of Orbit's Canvas panel is not ported).</summary>
    private BitmapSource RenderPicture()
    {
        _model.CommitText();
        var image = _item.Image;
        double scale = _model.PixelScale;
        var (x, y, w, h) = _model.Crop is { } crop
            ? AnnotationGeometry.PixelCrop(crop, scale, image.PixelWidth, image.PixelHeight)
            : (0, 0, image.PixelWidth, image.PixelHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-x / scale, -y / scale));
            _renderer.Draw(dc, 1, exporting: true);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void Copy()
    {
        var image = RenderPicture();
        Toast.Show(FrameImages.Copy(image, FrameImages.Png(image)) ? Strings.CaptureCopied : Strings.CopyFailed);
    }

    /// <summary>"… (Annotated).png" in the save folder, or wherever the user picks.</summary>
    private void Save(bool ask)
    {
        var folder = _preferences.Get<string>(Pref.CaptureSaveFolder);
        var name = Path.GetFileNameWithoutExtension(_item.Path) is { Length: > 0 } stem ? $"{stem} (Annotated).png" : "Orbit Annotated.png";
        string path;
        if (ask)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = name, DefaultExt = ".png", Filter = $"{Strings.EditorPngFiles}|*.png",
                InitialDirectory = Directory.Exists(folder) ? folder : "",
            };
            if (dialog.ShowDialog(this) != true) return;
            path = dialog.FileName;
        }
        else
        {
            path = Path.Combine(folder, name);
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, FrameImages.Png(RenderPicture()));
            Toast.Show(Strings.CaptureSaved, Path.GetFileName(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("An annotated capture could not be saved", e);
            Toast.Show(Strings.CaptureSaveFailed);
        }
    }

    private void Pin()
    {
        PinWindow.Pin(new CaptureItem("", RenderPicture()), _cards);
        Close();
    }

    private void Trash()
    {
        if (!_cards.Actions.Trash(_item)) return;
        // The card would come back when the editor closes; it's gone now.
        _returnCard = false;
        Close();
    }

    private void ToggleFloating()
    {
        bool on = !_preferences.Get<bool>(Pref.CaptureEditorFloating);
        _preferences.Set(Pref.CaptureEditorFloating, on);
        foreach (var editor in OpenEditors)
        {
            editor.Topmost = on;
            editor.Refresh();
        }
    }

    // MARK: Toolbar

    private FrameworkElement Toolbar()
    {
        var bar = new DockPanel { Height = 56, Background = ToolbarFill, LastChildFill = false };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        _floating.Margin = new Thickness(0, 0, 10, 0);
        left.Children.Add(_floating);

        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var group in Enum.GetValues<ToolGroup>()) tools.Children.Add(GroupButton(group));
        left.Children.Add(Capsule(tools));

        var colors = Capsule(_swatchStrip);
        colors.Margin = new Thickness(12, 0, 0, 0);
        left.Children.Add(colors);
        _colorButton.Margin = new Thickness(12, 0, 0, 0);
        left.Children.Add(_colorButton);
        var style = IconButton("", Strings.EditorStyle, () => { });
        style.Click += (_, _) => ShowStyle(style);
        style.Margin = new Thickness(12, 0, 0, 0);
        left.Children.Add(style);
        DockPanel.SetDock(left, Dock.Left);
        bar.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        right.Children.Add(_resetCrop);
        right.Children.Add(_undo);
        right.Children.Add(_redo);
        right.Children.Add(IconButton("", Strings.CardPin, Pin));
        right.Children.Add(IconButton("", Strings.CardMoveToRecycleBin, Trash));
        var save = PillButton(Strings.Save, Strings.EditorSaveHint, () => Save(ask: (Keyboard.Modifiers & ModifierKeys.Shift) != 0));
        save.Margin = new Thickness(10, 0, 0, 0);
        right.Children.Add(save);
        var copy = PillButton(Strings.ActionCopy, Strings.EditorCopyHint, Copy, prominent: true);
        copy.Margin = new Thickness(8, 0, 0, 0);
        right.Children.Add(copy);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right);
        return bar;
    }

    /// <summary>A narrow window folds the swatches into one color button.</summary>
    private void FitToolbar()
    {
        bool compact = ActualWidth < 1040;
        ((FrameworkElement)_swatchStrip.Parent).Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _colorButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Border Capsule(UIElement child) => new()
    {
        Background = GlassFill, CornerRadius = new CornerRadius(19), Padding = new Thickness(3), Child = child, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>One button per group: a click picks the group's last tool; the chevron or a right-click opens the rest.</summary>
    private Button GroupButton(ToolGroup group)
    {
        var grid = new Grid { Width = ToolSize, Height = ToolSize };
        grid.Children.Add(new TextBlock { FontFamily = Glyphs.Font, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        if (group.Tools().Count > 1)
        {
            var chevron = new TextBlock
            {
                Text = "", FontFamily = Glyphs.Font, FontSize = 7, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 4, 4), Background = Brushes.Transparent,
            };
            chevron.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                ShowGroup(group, grid);
            };
            grid.Children.Add(chevron);
        }
        var button = new Button
        {
            Content = grid, Width = ToolSize, Height = ToolSize, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Template = Controls.RoundTemplate(ToolSize / 2), Cursor = Cursors.Hand, Focusable = false,
        };
        button.Click += (_, _) => _model.Tool = _model.LastTool(group);
        button.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowGroup(group, button);
        };
        _groupButtons.Add((group, button));
        return button;
    }

    private void ShowGroup(ToolGroup group, UIElement under)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var tool in group.Tools())
        {
            var cell = new StackPanel { MinWidth = 56, Margin = new Thickness(2) };
            bool active = _model.Tool == tool;
            var icon = new Button
            {
                Content = new TextBlock { Text = ToolGlyph(tool), FontFamily = Glyphs.Font, FontSize = 15, Foreground = active ? Brushes.Black : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Width = ToolSize, Height = ToolSize, Background = active ? Brushes.White : Brushes.Transparent, BorderThickness = new Thickness(0),
                Template = Controls.RoundTemplate(ToolSize / 2), Cursor = Cursors.Hand, Focusable = false, HorizontalAlignment = HorizontalAlignment.Center,
            };
            var picked = tool;
            icon.Click += (_, _) =>
            {
                _model.Tool = picked;
                _popup.IsOpen = false;
            };
            cell.Children.Add(icon);
            cell.Children.Add(new TextBlock { Text = Strings.EditorToolTitle(tool), FontSize = 10, Foreground = Palette.DarkTheme.InkSecondary, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            cell.Children.Add(new TextBlock { Text = tool.Key().ToString(), FontSize = 9, FontFamily = new FontFamily("Cascadia Mono, Consolas"), Foreground = Palette.Fade(Brushes.White, 0.4), HorizontalAlignment = HorizontalAlignment.Center });
            row.Children.Add(cell);
        }
        OpenPopup(row, under);
    }

    private void ShowColors(UIElement under)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < EditorTools.Palette.Length; i++) row.Children.Add(Swatch(EditorTools.Palette[i], i + 1, () => _popup.IsOpen = false, track: false));
        OpenPopup(row, under);
    }

    /// <summary>Options for the tool in hand (or the selected mark).</summary>
    private void ShowStyle(UIElement under)
    {
        var tool = _model.StyleTool;
        var panel = new StackPanel { Width = 240 };
        panel.Children.Add(new TextBlock { Text = Strings.EditorToolTitle(tool), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 10) });
        switch (tool)
        {
            case EditorTool.Text:
                panel.Children.Add(Slider(Strings.EditorSize, _model.FontSize, 10, 120, v => _model.FontSize = v, "{0:0} pt"));
                panel.Children.Add(Segments(Strings.EditorWeight, Enum.GetValues<TextWeight>(), _model.Weight, Strings.EditorWeightName, v => _model.Weight = v));
                panel.Children.Add(Segments(Strings.EditorTextStyle, Enum.GetValues<TextStyle>(), _model.TextStyle, Strings.EditorTextStyleName, v => _model.TextStyle = v));
                panel.Children.Add(Note(Strings.EditorTextNote));
                break;
            case EditorTool.Arrow:
                panel.Children.Add(Slider(Strings.EditorThickness, _model.StrokeWidth, 1, 16, v => _model.StrokeWidth = v));
                panel.Children.Add(Segments(Strings.EditorShape, [false, true], _model.Curved, c => c ? Strings.EditorCurved : Strings.EditorStraight, v => _model.Curved = v));
                break;
            case EditorTool.Rect or EditorTool.Ellipse:
                panel.Children.Add(Slider(Strings.EditorThickness, _model.StrokeWidth, 1, 16, v => _model.StrokeWidth = v));
                panel.Children.Add(Check(Strings.EditorFilled, _model.Filled, v => _model.Filled = v));
                if (tool == EditorTool.Rect) panel.Children.Add(Slider(Strings.EditorCorners, _model.CornerRadius, 0, 40, v => _model.CornerRadius = v));
                break;
            case EditorTool.Line or EditorTool.Pen or EditorTool.Highlight or EditorTool.Measure:
                panel.Children.Add(Slider(Strings.EditorThickness, _model.StrokeWidth, 1, 16, v => _model.StrokeWidth = v));
                break;
            case EditorTool.Counter:
                panel.Children.Add(Slider(Strings.EditorSize, _model.StrokeWidth, 1, 16, v => _model.StrokeWidth = v));
                panel.Children.Add(Note(Strings.EditorCounterNote));
                break;
            case EditorTool.Loupe:
                panel.Children.Add(Slider(Strings.EditorSize, _model.StrokeWidth, 1, 16, v => _model.StrokeWidth = v));
                panel.Children.Add(Note(Strings.EditorLoupeNote));
                break;
            case EditorTool.Blur or EditorTool.Pixelate:
                panel.Children.Add(Slider(Strings.EditorStrength, _model.Strength, 1, 10, v => _model.Strength = v));
                break;
            case EditorTool.Erase:
                panel.Children.Add(Note(Strings.EditorEraseNote));
                break;
            case EditorTool.Spotlight:
                panel.Children.Add(Note(Strings.EditorSpotlightNote));
                break;
            case EditorTool.Picker:
                panel.Children.Add(Note(Strings.EditorPickerNote));
                break;
            default:
                panel.Children.Add(Note(Strings.EditorNothingNote));
                break;
        }
        OpenPopup(panel, under, padding: 14);
    }

    private void OpenPopup(UIElement content, UIElement under, double padding = 8)
    {
        _popup.Child = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 43)), BorderBrush = Palette.Fade(Brushes.White, 0.12), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(padding), Child = content, Margin = new Thickness(0, 6, 0, 0),
        };
        _popup.PlacementTarget = under;
        _popup.IsOpen = true;
    }

    private static FrameworkElement Slider(string title, double value, double min, double max, Action<double> set, string format = "{0:0}")
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var head = new DockPanel();
        var number = new TextBlock { Text = string.Format(format, value), FontSize = 12, Foreground = Palette.DarkTheme.InkSecondary };
        DockPanel.SetDock(number, Dock.Right);
        head.Children.Add(number);
        head.Children.Add(new TextBlock { Text = title, FontSize = 12, Foreground = Brushes.White });
        panel.Children.Add(head);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, IsSnapToTickEnabled = true, TickFrequency = 1, Margin = new Thickness(0, 4, 0, 0), Focusable = false };
        slider.ValueChanged += (_, e) =>
        {
            number.Text = string.Format(format, e.NewValue);
            set(e.NewValue);
        };
        panel.Children.Add(slider);
        return panel;
    }

    private static FrameworkElement Segments<T>(string title, IReadOnlyList<T> options, T current, Func<T, string> label, Action<T> set)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 12, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 4) });
        var row = new UniformGrid { Rows = 1 };
        var buttons = new List<Button>();
        foreach (var option in options)
        {
            var value = option;
            var button = new Button
            {
                Content = new TextBlock { Text = label(option), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center },
                Height = 26, Margin = new Thickness(1), BorderThickness = new Thickness(0), Template = Controls.RoundTemplate(6), Cursor = Cursors.Hand, Focusable = false,
            };
            void Paint(bool on)
            {
                button.Background = on ? Brushes.White : GlassFill;
                ((TextBlock)button.Content).Foreground = on ? Brushes.Black : Brushes.White;
            }
            Paint(EqualityComparer<T>.Default.Equals(option, current));
            button.Click += (_, _) =>
            {
                foreach (var b in buttons) { b.Background = GlassFill; ((TextBlock)b.Content).Foreground = Brushes.White; }
                Paint(true);
                set(value);
            };
            buttons.Add(button);
            row.Children.Add(button);
        }
        panel.Children.Add(row);
        return panel;
    }

    private static FrameworkElement Check(string title, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = new TextBlock { Text = title, FontSize = 12, Foreground = Brushes.White }, IsChecked = value, Margin = new Thickness(0, 0, 0, 10), Focusable = false };
        box.Click += (_, _) => set(box.IsChecked == true);
        return box;
    }

    private static TextBlock Note(string text) => new() { Text = text, FontSize = 11, Foreground = Palette.DarkTheme.InkSecondary, TextWrapping = TextWrapping.Wrap };

    private FrameworkElement Swatch(uint color, int index, Action? after, bool track = true)
    {
        var ring = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent,
            Child = Dot(color, 16), Cursor = Cursors.Hand, ToolTip = Strings.EditorColor(index), Background = Brushes.Transparent, Margin = new Thickness(1, 0, 1, 0),
        };
        ring.MouseLeftButtonUp += (_, _) =>
        {
            _model.Color = color;
            after?.Invoke();
        };
        if (track) _swatches.Add((color, ring));
        return ring;
    }

    private static FrameworkElement Dot(uint color, double size) => new System.Windows.Shapes.Ellipse
    {
        Width = size, Height = size, Fill = AnnotationRenderer.ToBrush(color), Stroke = Palette.Fade(Brushes.White, 0.35), StrokeThickness = 0.5,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    private static Button IconButton(string glyph, string help, Action click)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = Glyphs.Font, FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Width = ToolSize, Height = ToolSize, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Template = Controls.RoundTemplate(ToolSize / 2), Cursor = Cursors.Hand, ToolTip = help, Focusable = false,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, help);
        button.MouseEnter += (_, _) => button.Background = HoverFill;
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.Click += (_, _) => click();
        return button;
    }

    private static Button PillButton(string title, string help, Action click, bool prominent = false)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = prominent ? Brushes.Black : Brushes.White },
            Height = 30, Padding = new Thickness(14, 0, 14, 0), Background = prominent ? Brushes.White : GlassFill, BorderThickness = new Thickness(0),
            Template = Controls.RoundTemplate(15), Cursor = Cursors.Hand, ToolTip = help, Focusable = false,
        };
        button.Click += (_, _) => click();
        return button;
    }

    public static string ToolGlyph(EditorTool tool) => tool switch
    {
        EditorTool.Select => "",
        EditorTool.Arrow => "",
        EditorTool.Line => "",
        EditorTool.Rect => "",
        EditorTool.Ellipse => "",
        EditorTool.Pen => "",
        EditorTool.Highlight => "",
        EditorTool.Text => "",
        EditorTool.Counter => "",
        EditorTool.Blur => "",
        EditorTool.Pixelate => "",
        EditorTool.Erase => "",
        EditorTool.Spotlight => "",
        EditorTool.Loupe => "",
        EditorTool.Measure => "",
        EditorTool.Picker => "",
        _ => "",
    };
}
