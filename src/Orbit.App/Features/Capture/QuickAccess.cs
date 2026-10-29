using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Orbit.App.Design;
using Orbit.App.Overlays;
using Orbit.Core.Capture;
using Orbit.Core.Display;
using Orbit.Core.Settings;

namespace Orbit.App.Features.Capture;

/// <summary>
/// The preview cards after a capture (Orbit: QuickAccess.swift), in a corner of the monitor under
/// the pointer, newest nearest the corner. Never takes focus. A card goes after
/// capture.thumbnailSeconds unless hovered, kept or tucked; click edits, a swipe toward the edge
/// dismisses, a drag any other way carries the file out; right-click has the rest.
/// </summary>
internal sealed class QuickAccess(Preferences preferences, CaptureActions actions)
{
    private readonly CardStack<CaptureItem> _stack = new();
    private readonly Dictionary<CaptureItem, DispatcherTimer> _timers = [];
    private readonly Dictionary<CaptureItem, CaptureCard> _cards = [];
    private QuickAccessPanel? _panel;
    private MonitorInfo? _monitor;

    public CaptureActions Actions { get; } = actions;

    /// <summary>Opens the editor on a capture; null until the editor exists (the file opens in its app then).</summary>
    public Action<CaptureItem>? Edit { get; set; }

    /// <summary>Pins a capture to the screen.</summary>
    public Action<CaptureItem>? Pin { get; set; }

    public CardCorner Corner => CardCorner.Parse(preferences.Get<string>(Pref.CaptureThumbnailCorner));

    private double Seconds => preferences.Get<double>(Pref.CaptureThumbnailSeconds);

    /// <summary>A card on top. <paramref name="keep"/>: it stays until closed by hand (back from the editor or a pin).</summary>
    public void Add(CaptureItem item, bool keep = false)
    {
        var (x, y) = Monitors.Cursor();
        _monitor = Monitors.FromPoint(x, y) ?? _monitor;
        foreach (var gone in _stack.Add(item, keep)) Forget(gone);
        Schedule(item);
        Layout();
    }

    public void Dismiss(CaptureItem item)
    {
        if (!_stack.Dismiss(item)) return;
        Forget(item);
        Layout();
    }

    public void SetTucked(bool tucked)
    {
        _stack.SetTucked(tucked);
        foreach (var item in _stack.Items)
        {
            if (tucked) StopTimer(item);
            else Schedule(item);
        }
        Layout();
    }

    public void SetHover(CaptureItem item, bool on)
    {
        if (!_stack.Items.Contains(item)) return;
        _stack.SetHover(item, on);
        if (on) StopTimer(item);
        else Schedule(item);
    }

    /// <summary>The card is in use (dragged out, its menu open, its text being read): it stays meanwhile.</summary>
    public void Hold(CaptureItem item)
    {
        _stack.Hold(item);
        StopTimer(item);
    }

    public void Release(CaptureItem item)
    {
        if (_stack.Release(item))
        {
            Forget(item);
            Layout();
        }
        else if (_stack.Items.Contains(item)) Schedule(item);
    }

    /// <summary>"Preview" in settings: a real card at the chosen corner for a couple of seconds.</summary>
    public void ShowSample()
    {
        var item = Actions.Store.Recent.Select(CaptureItem.Load).FirstOrDefault(i => i is not null) ?? new CaptureItem("", SampleImage());
        Add(item, keep: true);
        Later.Run(TimeSpan.FromSeconds(2), () =>
        {
            if (!_stack.IsHovered(item)) Dismiss(item);
        });
    }

    /// <summary>A plain picture for the sample when there is no capture yet.</summary>
    private static System.Windows.Media.Imaging.BitmapSource SampleImage()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(40, 40, 44), Color.FromRgb(90, 90, 96), 45), null, new Rect(0, 0, 480, 270));
            dc.DrawEllipse(null, new Pen(Brushes.White, 10), new Point(240, 135), 40, 40);
        }
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(480, 270, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // MARK: Card actions

    /// <summary>A click on a card: the editor; closing it brings the card back, kept.</summary>
    public void Open(CaptureItem item)
    {
        Dismiss(item);
        if (Edit is { } edit) edit(item);
        else if (File.Exists(item.Path)) Shell.Open(item.Path);
        else Toast.Show(Strings.CaptureFileMissing);
    }

    public void Copy(CaptureItem item)
    {
        Actions.Copy(item);
        Dismiss(item);
    }

    public void Save(CaptureItem item)
    {
        Actions.Save(item);
        Dismiss(item);
    }

    public void PinIt(CaptureItem item)
    {
        Dismiss(item);
        Pin?.Invoke(item);
    }

    public void Trash(CaptureItem item)
    {
        if (Actions.Trash(item)) Dismiss(item);
    }

    public async void CopyText(CaptureItem item)
    {
        Hold(item);
        try
        {
            await CaptureActions.RecognizeAsync(item.Image);
        }
        finally
        {
            Release(item);
        }
    }

    // MARK: Timers

    private void Schedule(CaptureItem item)
    {
        StopTimer(item);
        double seconds = Seconds;
        if (!_stack.ShouldSchedule(item, seconds)) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) =>
        {
            StopTimer(item);
            if (_stack.AutoDismissDue(item, Seconds)) Dismiss(item);
        };
        _timers[item] = timer;
        timer.Start();
    }

    private void StopTimer(CaptureItem item)
    {
        if (!_timers.Remove(item, out var timer)) return;
        timer.Stop();
    }

    private void Forget(CaptureItem item)
    {
        StopTimer(item);
        _cards.Remove(item);
    }

    // MARK: Layout

    /// <summary>Sizes the panel to its cards so the empty area never blocks clicks.</summary>
    private void Layout()
    {
        if (_stack.Items.Count == 0 || _monitor is not { } monitor)
        {
            _panel?.Hide();
            return;
        }
        _panel ??= new QuickAccessPanel();
        var corner = Corner;
        double scale = monitor.Scale;
        if (_stack.Tucked)
        {
            var tab = TuckTab(_stack.Items[0], _stack.Items.Count, corner, Palette.Current());
            _panel.Present(tab, CardLayout.TabWidth, CardLayout.TabHeight,
                CardLayout.Place(monitor.WorkArea, scale, corner, CardLayout.TabWidth, CardLayout.TabHeight, tucked: true));
            return;
        }

        var cards = _stack.Items.Select(item => Card(item, scale, corner)).ToList();
        var heights = cards.Select(c => c.Height).ToList();
        int fitting = CardLayout.Fitting(heights, monitor.WorkArea.Height / scale);
        cards = cards.Take(fitting).ToList();
        heights = heights.Take(fitting).ToList();
        // Newest nearest the corner.
        if (!corner.OnTop) cards.Reverse();
        var stack = new StackPanel { Margin = new Thickness(CardLayout.Padding) };
        foreach (var card in cards)
        {
            if (card.Parent is Panel old) old.Children.Remove(card);
            card.Margin = new Thickness(0, stack.Children.Count == 0 ? 0 : CardLayout.Gap, 0, 0);
            stack.Children.Add(card);
        }
        var (width, height) = CardLayout.PanelSize(heights);
        _panel.Present(stack, width, height, CardLayout.Place(monitor.WorkArea, scale, corner, width, height, tucked: false));
    }

    /// <summary>The card for an item, kept between layouts so a hovered card stays as it is.</summary>
    private CaptureCard Card(CaptureItem item, double scale, CardCorner corner)
    {
        if (_cards.TryGetValue(item, out var card) && card.Scale == scale && card.OnLeft == corner.OnLeft) return card;
        card = new CaptureCard(this, item, scale, corner.OnLeft);
        _cards[item] = card;
        return card;
    }

    /// <summary>What's left of the cards when tucked: a tab on the side edge with the newest one in it.</summary>
    private FrameworkElement TuckTab(CaptureItem newest, int count, CardCorner corner, Palette palette)
    {
        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        column.Children.Add(new Border
        {
            Width = 20, Height = 26, CornerRadius = new CornerRadius(3), ClipToBounds = true,
            Child = new System.Windows.Controls.Image { Source = newest.Image, Stretch = Stretch.Uniform },
        });
        column.Children.Add(new TextBlock
        {
            Text = corner.OnLeft ? "" : "", FontFamily = Glyphs.Font, FontSize = 12, Foreground = palette.Ink,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0),
        });
        if (count > 1)
            column.Children.Add(new TextBlock { Text = count.ToString(), FontSize = 9, FontWeight = FontWeights.Bold, Foreground = palette.Ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
        var tab = new Border
        {
            Width = CardLayout.TabWidth, Height = CardLayout.TabHeight, Background = palette.Surface, BorderBrush = palette.Hairline, BorderThickness = new Thickness(1),
            // Square on the screen-edge side, rounded on the other.
            CornerRadius = corner.OnLeft ? new CornerRadius(0, 10, 10, 0) : new CornerRadius(10, 0, 0, 10),
            Child = column, Cursor = Cursors.Hand, ToolTip = Strings.CardShowAll,
        };
        tab.MouseEnter += (_, _) => tab.Background = palette.Raised;
        tab.MouseLeave += (_, _) => tab.Background = palette.Surface;
        tab.MouseLeftButtonUp += (_, _) => SetTucked(false);
        return tab;
    }
}

/// <summary>The window the cards sit in: transparent, topmost, never activated.</summary>
internal sealed class QuickAccessPanel : OverlayWindow
{
    public QuickAccessPanel() : base(activating: false, transparent: true, clickThrough: false) { }

    /// <summary>Shows <paramref name="content"/> (<paramref name="width"/> × <paramref name="height"/> DIPs) at <paramref name="rect"/>.</summary>
    public void Present(FrameworkElement content, double width, double height, PixelRect rect)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        Content = content;
        if (IsVisible) Place(rect);
        else ShowAt(rect);
    }
}

/// <summary>One preview card (Orbit: <c>ThumbnailCard</c>, <c>SwipeOrDrag</c>).</summary>
internal sealed class CaptureCard : Grid
{
    private const double Radius = 12;
    private static readonly Brush CardFill = Controls.Frozen(new SolidColorBrush(Color.FromRgb(31, 31, 31)));
    private static readonly Brush HoverDim = Controls.Frozen(new SolidColorBrush(Color.FromArgb(82, 0, 0, 0)));
    private static readonly Brush Glass = Controls.Frozen(new SolidColorBrush(Color.FromArgb(56, 255, 255, 255)));
    private static readonly Brush GlassHover = Controls.Frozen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)));
    private static readonly Brush HintInk = Controls.Frozen(new SolidColorBrush(Color.FromArgb(204, 255, 255, 255)));

    private readonly QuickAccess _owner;
    private readonly CaptureItem _item;
    private readonly Grid _hover;
    private readonly TextBlock? _sizeLabel;
    private readonly TranslateTransform _shift = new();
    private bool _hovered;
    private Point? _press;
    private CardGesture? _gesture;

    public CaptureCard(QuickAccess owner, CaptureItem item, double scale, bool onLeft)
    {
        _owner = owner;
        _item = item;
        Scale = scale;
        OnLeft = onLeft;
        double imageW = item.Image.PixelWidth / scale, imageH = item.Image.PixelHeight / scale;
        Width = CardLayout.Width;
        Height = CardLayout.Height(imageW, imageH);
        RenderTransform = _shift;
        Background = Brushes.Transparent;

        Children.Add(new Border
        {
            Background = CardFill, CornerRadius = new CornerRadius(Radius),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.28, Color = Colors.Black },
        });
        var content = new Grid { Clip = new RectangleGeometry(new Rect(0, 0, Width, Height), Radius, Radius) };
        Children.Add(content);
        var (fitW, fitH) = CardLayout.Fit(imageW, imageH);
        bool small = CardLayout.IsSmall(imageW, imageH);
        var image = new System.Windows.Controls.Image
        {
            Source = item.Image, Width = fitW, Height = fitH, Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(image, small ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        content.Children.Add(image);
        TextBlock? sizeLabel = null;
        if (small)
        {
            sizeLabel = new TextBlock
            {
                Text = $"{item.Image.PixelWidth} × {item.Image.PixelHeight}", FontSize = 9, FontWeight = FontWeights.SemiBold,
                Foreground = HintInk, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6),
            };
            content.Children.Add(sizeLabel);
        }
        _hover = HoverControls();
        content.Children.Add(_hover);
        Children.Add(new Border
        {
            CornerRadius = new CornerRadius(Radius), BorderThickness = new Thickness(0.5), IsHitTestVisible = false,
            BorderBrush = Controls.Frozen(new SolidColorBrush(Color.FromArgb(56, 255, 255, 255))),
        });

        _sizeLabel = sizeLabel;
        MouseEnter += (_, _) => SetHovered(true);
        MouseLeave += (_, _) =>
        {
            if (!IsMouseCaptured) SetHovered(false);
        };
        MouseLeftButtonDown += Pressed;
        MouseMove += Moved;
        MouseLeftButtonUp += Released;
        LostMouseCapture += (_, _) => CancelPress();
        ContextMenu = Menu();

        // In from the side edge.
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)));
        _shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(onLeft ? -40 : 40, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    public double Scale { get; }
    public bool OnLeft { get; }

    private void SetHovered(bool on)
    {
        if (on == _hovered) return;
        _hovered = on;
        _hover.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (_sizeLabel is not null) _sizeLabel.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        _owner.SetHover(_item, on);
    }

    /// <summary>After the mouse was captured (a swipe, a drag), hover is wherever the pointer ended up.</summary>
    private void SyncHover() => SetHovered(IsMouseOver);

    // MARK: Gesture

    private void Pressed(object sender, MouseButtonEventArgs e)
    {
        _press = e.GetPosition(Window.GetWindow(this));
        _gesture = new CardGesture(OnLeft ? -1 : 1);
        CaptureMouse();
        e.Handled = true;
    }

    private void Moved(object sender, MouseEventArgs e)
    {
        if (_press is not { } start || _gesture is not { } gesture) return;
        var p = e.GetPosition(Window.GetWindow(this));
        double dx = p.X - start.X, dy = p.Y - start.Y;
        switch (gesture.Move(dx, dy))
        {
            case CardGestureKind.Swipe:
                double offset = gesture.Offset(dx);
                _shift.BeginAnimation(TranslateTransform.XProperty, null);
                _shift.X = offset;
                Opacity = 1 - Math.Min(0.7, Math.Abs(offset) / 260);
                break;
            case CardGestureKind.Drag:
                _press = null;
                _gesture = null;
                ReleaseMouseCapture();
                CarryOut();
                break;
        }
    }

    private void Released(object sender, MouseButtonEventArgs e)
    {
        if (_press is not { } start || _gesture is not { } gesture) return;
        double dx = e.GetPosition(Window.GetWindow(this)).X - start.X;
        _press = null;
        _gesture = null;
        ReleaseMouseCapture();
        SyncHover();
        e.Handled = true;
        switch (gesture.Kind)
        {
            case CardGestureKind.Pending:
                _owner.Open(_item);
                break;
            case CardGestureKind.Swipe when gesture.Dismisses(dx):
                _owner.Dismiss(_item);
                break;
            default:
                SpringBack();
                break;
        }
    }

    /// <summary>The press ended some other way (another window took the mouse): the card goes back.</summary>
    private void CancelPress()
    {
        if (_press is null) return;
        _press = null;
        _gesture = null;
        SyncHover();
        SpringBack();
    }

    private void SpringBack()
    {
        _shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut } });
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
    }

    /// <summary>The file goes out with the pointer (OLE FileDrop); a drop somewhere takes the card away.</summary>
    private void CarryOut()
    {
        _shift.BeginAnimation(TranslateTransform.XProperty, null);
        _shift.X = 0;
        if (!File.Exists(_item.Path))
        {
            SpringBack();
            Toast.Show(Strings.CaptureFileMissing);
            return;
        }
        _owner.Hold(_item);
        var effect = DragDropEffects.None;
        try
        {
            // No drag image of the card: the card stays in place, faded, while its file travels.
            BeginAnimation(OpacityProperty, null);
            Opacity = 0.35;
            var data = new DataObject(DataFormats.FileDrop, new[] { _item.Path });
            effect = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
        }
        finally
        {
            Opacity = 1;
            SyncHover();
            _owner.Release(_item);
        }
        if (effect != DragDropEffects.None) _owner.Dismiss(_item);
    }

    // MARK: Controls

    /// <summary>Just the essentials, so most of the card is free to drag out; the rest is on right-click.</summary>
    private Grid HoverControls()
    {
        var layer = new Grid { Visibility = Visibility.Collapsed };
        layer.Children.Add(new Border { Background = HoverDim, IsHitTestVisible = false });
        var column = new Grid { Margin = new Thickness(8) };
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition());
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new DockPanel { LastChildFill = false };
        var pin = IconButton("", Strings.CardPin, () => _owner.PinIt(_item));
        DockPanel.SetDock(pin, Dock.Left);
        top.Children.Add(pin);
        var close = IconButton(Glyphs.Close, Strings.CardClose, () => _owner.Dismiss(_item));
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(close);
        var trash = IconButton("", Strings.CardMoveToRecycleBin, () => _owner.Trash(_item));
        trash.Margin = new Thickness(0, 0, 6, 0);
        DockPanel.SetDock(trash, Dock.Right);
        top.Children.Add(trash);
        column.Children.Add(top);

        var middle = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        middle.Children.Add(TextButton(Strings.ActionCopy, () => _owner.Copy(_item)));
        var save = TextButton(Strings.Save, () => _owner.Save(_item));
        save.Margin = new Thickness(8, 0, 0, 0);
        middle.Children.Add(save);
        Grid.SetRow(middle, 1);
        column.Children.Add(middle);

        var hint = new TextBlock
        {
            Text = Strings.CardHint, FontSize = 11, Foreground = HintInk, HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false,
        };
        Grid.SetRow(hint, 2);
        column.Children.Add(hint);
        layer.Children.Add(column);
        return layer;
    }

    private static Button IconButton(string glyph, string help, Action click) =>
        GlassButton(new TextBlock { Text = glyph, FontFamily = Glyphs.Font, FontSize = 11, Foreground = Brushes.White }, 26, 26, 13, help, click);

    private static Button TextButton(string title, Action click) =>
        GlassButton(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.Medium, Foreground = Brushes.White }, 76, 30, 15, title, click);

    private static Button GlassButton(TextBlock content, double width, double height, double radius, string help, Action click)
    {
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        var button = new Button
        {
            Content = content, Width = width, Height = height, Background = Glass, BorderThickness = new Thickness(0),
            Template = Controls.RoundTemplate(radius), Cursor = Cursors.Hand, ToolTip = help, Focusable = false,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, help);
        button.MouseEnter += (_, _) => button.Background = GlassHover;
        button.MouseLeave += (_, _) => button.Background = Glass;
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>The right-click menu: the actions that would crowd the card as buttons.</summary>
    private ContextMenu Menu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Controls.MenuItem(Strings.CardEdit, () => _owner.Open(_item)));
        menu.Items.Add(Controls.MenuItem(Strings.CardCopyText, () => _owner.CopyText(_item)));
        menu.Items.Add(Controls.MenuItem(Strings.CardPin, () => _owner.PinIt(_item)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Controls.MenuItem(Strings.ActionCopy, () => _owner.Copy(_item)));
        menu.Items.Add(Controls.MenuItem(Strings.CardSaveToFolder, () => _owner.Save(_item)));
        menu.Items.Add(Controls.MenuItem(Strings.CardShowInExplorer, () => _owner.Actions.Reveal(_item)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Controls.MenuItem(Strings.CardTuck, () => _owner.SetTucked(true)));
        menu.Items.Add(Controls.MenuItem(Strings.CardClose, () => _owner.Dismiss(_item)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Controls.MenuItem(Strings.CardMoveToRecycleBin, () => _owner.Trash(_item)));
        menu.Opened += (_, _) => _owner.Hold(_item);
        menu.Closed += (_, _) => _owner.Release(_item);
        return menu;
    }
}
