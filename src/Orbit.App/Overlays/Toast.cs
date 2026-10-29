using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Orbit.Core.Display;

namespace Orbit.App.Overlays;

/// <summary>
/// A small capsule confirming an action ("텍스트를 복사했습니다"), at the bottom of the monitor
/// under the pointer. Never takes focus or clicks; gone after 1.6 s (Orbit: Core/Toast.swift).
/// One at a time: a new one replaces the old.
/// </summary>
public sealed class Toast : OverlayWindow
{
    private static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(1.6);
    /// <summary>Orbit's 64 pt above the bottom of the visible frame.</summary>
    private const double BottomDip = 64;

    private static Toast? _current;

    private readonly DispatcherTimer _timer = new() { Interval = ShowFor };
    private readonly Border _body;

    private Toast(string message, string? detail) : base(activating: false, transparent: true, clickThrough: true)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = message, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        if (detail is not null)
            text.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(190, 190, 190)) });
        _body = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 20, 20, 20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 9, 18, 10),
            Child = text,
        };
        Content = _body;
        _timer.Tick += (_, _) => FadeOut();
    }

    public static void Show(string message, string? detail = null)
    {
        _current?.Close();
        var toast = new Toast(message, detail);
        _current = toast;
        toast.Closed += (_, _) => { if (_current == toast) _current = null; };

        var (x, y) = Monitors.Cursor();
        var monitor = Monitors.FromPoint(x, y);
        if (monitor is null) return;
        // Measured in DIPs at the monitor's scale, then placed in pixels.
        toast._body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = toast._body.DesiredSize;
        int width = (int)Math.Ceiling(size.Width * monitor.Scale), height = (int)Math.Ceiling(size.Height * monitor.Scale);
        var work = monitor.WorkArea;
        int left = work.X + (work.Width - width) / 2;
        int top = work.Bottom - height - (int)Math.Round(BottomDip * monitor.Scale);
        toast.ShowAt(new PixelRect(left, top, width, height));
        toast._timer.Start();
    }

    private void FadeOut()
    {
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
