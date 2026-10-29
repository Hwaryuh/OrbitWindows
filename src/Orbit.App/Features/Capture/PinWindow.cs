using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Orbit.App.Design;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Capture;
using Orbit.Core.Display;
using Windows.Win32;

namespace Orbit.App.Features.Capture;

/// <summary>
/// A capture floating above everything (Orbit: PinWindow.swift). Drag to move, the wheel changes
/// its opacity, Ctrl+wheel (and a touchpad pinch, which arrives as Ctrl+wheel) or an edge resizes
/// it with its ratio kept; a double-click or Esc closes it. Unpin puts it back among the cards.
/// </summary>
internal sealed class PinWindow : OverlayWindow
{
    private const int WmNcHitTest = 0x84, WmSizing = 0x214;
    private const int HtLeft = 10, HtRight = 11, HtTop = 12, HtTopLeft = 13, HtTopRight = 14, HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;
    /// <summary>The band along the edges that resizes, in DIPs.</summary>
    private const double EdgeBand = 8;
    private const double Corner = 8;

    private static readonly HashSet<PinWindow> Open = [];

    private readonly CaptureItem _item;
    private readonly QuickAccess _cards;
    private readonly double _ratio;
    private readonly Border _edge;
    private readonly StackPanel _buttons;

    private PinWindow(CaptureItem item, QuickAccess cards) : base(activating: true, transparent: true, clickThrough: false)
    {
        _item = item;
        _cards = cards;
        _ratio = (double)item.Image.PixelWidth / Math.Max(1, item.Image.PixelHeight);
        // WS_THICKFRAME, so Windows runs its own resize loop for the edges WM_NCHITTEST reports.
        ResizeMode = ResizeMode.CanResize;

        var image = new System.Windows.Controls.Image { Source = item.Image, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var root = new Grid { Background = Brushes.Transparent };
        var picture = new Border { CornerRadius = new CornerRadius(Corner), Child = image, ClipToBounds = true };
        picture.SizeChanged += (_, e) => image.Clip = new RectangleGeometry(new Rect(e.NewSize), Corner, Corner);
        root.Children.Add(picture);
        _edge = new Border
        {
            CornerRadius = new CornerRadius(Corner), BorderThickness = new Thickness(1.5), BorderBrush = Palette.DarkTheme.Accent,
            Opacity = 0, IsHitTestVisible = false,
        };
        root.Children.Add(_edge);
        _buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6), Visibility = Visibility.Collapsed,
        };
        _buttons.Children.Add(RoundButton("", Strings.PinUnpinHint, Strings.PinUnpin, Unpin));
        var close = RoundButton(Glyphs.Close, Strings.PinCloseHint, Strings.CardClose, Close);
        close.Margin = new Thickness(6, 0, 0, 0);
        _buttons.Children.Add(close);
        root.Children.Add(_buttons);
        Content = root;

        root.MouseEnter += (_, _) => Hover(true);
        root.MouseLeave += (_, _) => Hover(false);
        root.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) Close();
            else DragMove();
        };
        MouseWheel += Wheel;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) cards.Actions.Copy(_item);
        };
        ContextMenu = Menu();
        SourceInitialized += (_, _) => HwndSource.FromHwnd(Handle).AddHook(Hook);
        Closed += (_, _) => Open.Remove(this);
    }

    /// <summary>A new pin at one screen pixel per image pixel, shrunk to fit, centered on the monitor under the pointer.</summary>
    public static void Pin(CaptureItem item, QuickAccess cards)
    {
        var (x, y) = Monitors.Cursor();
        if (Monitors.FromPoint(x, y) is not { } monitor) return;
        var window = new PinWindow(item, cards);
        Open.Add(window);
        window.ShowAt(PinLayout.Initial(item.Image.PixelWidth, item.Image.PixelHeight, monitor.WorkArea));
        window.Activate();
    }

    protected override bool HoldsPlacement => false;

    private void Hover(bool on)
    {
        _edge.Opacity = on ? 0.8 : 0;
        _buttons.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Wheel(object sender, MouseWheelEventArgs e)
    {
        double notches = e.Delta / 120.0;
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            var monitors = Monitors.All();
            int minSide = (int)Math.Round(PinLayout.MinSide * CurrentScale());
            Place(PinLayout.Zoom(CurrentRect(), Math.Pow(1.1, notches), minSide, Monitors.VirtualScreen(monitors)));
        }
        else
        {
            Opacity = PinLayout.Opacity(Opacity, notches);
        }
        e.Handled = true;
    }

    /// <summary>Back into the cards, kept, so it can be copied, saved, edited or pinned again.</summary>
    private void Unpin()
    {
        var item = _item;
        if (!File.Exists(item.Path))
        {
            // Thrown away meanwhile: the card needs a file to share, so the picture goes back into the library.
            try
            {
                item = new CaptureItem(_cards.Actions.Store.Write(FrameImages.Png(item.Image), DateTime.Now), item.Image);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error("An unpinned capture could not be written to the library", e);
                Toast.Show(Strings.CaptureSaveFailed);
                return;
            }
        }
        Close();
        _cards.Add(item, keep: true);
    }

    private ContextMenu Menu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Controls.MenuItem(Strings.ActionCopy, () => _cards.Actions.Copy(_item)));
        menu.Items.Add(Controls.MenuItem(Strings.CardEdit, () =>
        {
            Close();
            if (_cards.Edit is { } edit) edit(_item);
            else _cards.Open(_item);
        }));
        menu.Items.Add(Controls.MenuItem(Strings.PinUnpin, Unpin));
        menu.Items.Add(new Separator());
        menu.Items.Add(Controls.MenuItem(Strings.CardClose, Close));
        return menu;
    }

    private static Button RoundButton(string glyph, string help, string name, Action click)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = Glyphs.Font, FontSize = 10, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Width = 22, Height = 22, Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), BorderThickness = new Thickness(0),
            Template = Controls.RoundTemplate(11), Cursor = Cursors.Hand, ToolTip = help, Focusable = false,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    // MARK: Resizing

    private double CurrentScale() => VisualTreeHelper.GetDpi(this).DpiScaleX;

    private PixelRect CurrentRect()
    {
        PInvoke.GetWindowRect(Handle, out var r);
        return new PixelRect(r.left, r.top, r.right - r.left, r.bottom - r.top);
    }

    private unsafe nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case WmNcHitTest:
            {
                int x = (short)(lParam & 0xFFFF), y = (short)((lParam >> 16) & 0xFFFF);
                var rect = CurrentRect();
                int band = (int)Math.Round(EdgeBand * CurrentScale());
                bool left = x < rect.X + band, right = x >= rect.Right - band, top = y < rect.Y + band, bottom = y >= rect.Bottom - band;
                int hit = (top, bottom, left, right) switch
                {
                    (true, _, true, _) => HtTopLeft,
                    (true, _, _, true) => HtTopRight,
                    (_, true, true, _) => HtBottomLeft,
                    (_, true, _, true) => HtBottomRight,
                    (true, _, _, _) => HtTop,
                    (_, true, _, _) => HtBottom,
                    (_, _, true, _) => HtLeft,
                    (_, _, _, true) => HtRight,
                    _ => 0,
                };
                if (hit == 0) return 0;
                handled = true;
                return hit;
            }
            case WmSizing:
            {
                var r = (Windows.Win32.Foundation.RECT*)lParam;
                var proposed = new PixelRect(r->left, r->top, r->right - r->left, r->bottom - r->top);
                int minSide = (int)Math.Round(PinLayout.MinSide * CurrentScale());
                var kept = PinLayout.KeepRatio((SizingEdge)(int)wParam, proposed, _ratio, minSide);
                *r = new Windows.Win32.Foundation.RECT(kept.X, kept.Y, kept.Right, kept.Bottom);
                handled = true;
                return 1;
            }
        }
        return 0;
    }
}
