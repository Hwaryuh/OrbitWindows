using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Orbit.App.Design;
using Orbit.App.Overlays;
using Orbit.Core.Display;

namespace Orbit.App.Features.Capture;

/// <summary>
/// What shows while recording (Orbit: RecordingChrome.swift): a small bar with a red dot, the time,
/// stop and discard, and a dashed frame around a recorded area. Both stay out of the video.
/// </summary>
internal sealed class RecordingChrome
{
    private const double BarWidth = 168, BarHeight = 40, Gap = 12;

    private readonly ControlBar _bar;
    private readonly AreaFrame? _frame;

    private RecordingChrome(ControlBar bar, AreaFrame? frame)
    {
        _bar = bar;
        _frame = frame;
    }

    /// <param name="area">What is recorded, in physical pixels; null for a window (it may move).</param>
    /// <param name="anchor">Where the bar goes: below this rectangle, or above it, or inside at the bottom.</param>
    public static RecordingChrome Show(MonitorInfo monitor, PixelRect anchor, PixelRect? area, Action stop, Action discard)
    {
        var bar = new ControlBar(stop, discard);
        bar.ShowAt(BarPlace(monitor, anchor));
        bar.ExcludeFromCapture();
        AreaFrame? frame = null;
        if (area is { } a && a != monitor.Bounds)
        {
            frame = new AreaFrame(monitor.Scale);
            int pad = (int)Math.Ceiling(3 * monitor.Scale);
            frame.ShowAt(new PixelRect(a.X - pad, a.Y - pad, a.Width + 2 * pad, a.Height + 2 * pad));
            frame.ExcludeFromCapture();
        }
        return new RecordingChrome(bar, frame);
    }

    public void Close()
    {
        _bar.Close();
        _frame?.Close();
    }

    /// <summary>Below the anchor when there is room on its monitor, else above it, else inside at the bottom.</summary>
    internal static PixelRect BarPlace(MonitorInfo monitor, PixelRect anchor)
    {
        double scale = monitor.Scale;
        int width = (int)Math.Round(BarWidth * scale), height = (int)Math.Round(BarHeight * scale), gap = (int)Math.Round(Gap * scale);
        var work = monitor.WorkArea;
        int x = Math.Clamp(anchor.X + (anchor.Width - width) / 2, work.X, work.Right - width);
        int y;
        if (anchor.Bottom + gap + height <= work.Bottom) y = anchor.Bottom + gap;
        else if (anchor.Y - gap - height >= work.Y) y = anchor.Y - gap - height;
        else y = Math.Min(anchor.Bottom, work.Bottom) - gap * 2 - height;
        return new PixelRect(x, y, width, height);
    }

    private sealed class ControlBar : OverlayWindow
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly DispatcherTimer _timer;
        private readonly TextBlock _time;

        public ControlBar(Action stop, Action discard) : base(activating: false, transparent: true, clickThrough: false)
        {
            _time = new TextBlock
            {
                Text = CaptureCard.Clock(TimeSpan.Zero), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), MinWidth = 44,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 8, 0) };
            row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Color.FromRgb(255, 69, 58)), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(_time);
            row.Children.Add(Button("", Strings.RecordStop, stop));
            row.Children.Add(Button("", Strings.RecordDiscard, discard));
            Content = new Border
            {
                Width = BarWidth, Height = BarHeight, CornerRadius = new CornerRadius(BarHeight / 2),
                Background = new SolidColorBrush(Color.FromArgb(235, 28, 28, 30)), BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1), Child = row,
            };
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += (_, _) => _time.Text = CaptureCard.Clock(_clock.Elapsed);
            _timer.Start();
            Closed += (_, _) => _timer.Stop();
        }

        private static Button Button(string glyph, string help, Action click)
        {
            var normal = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
            var hover = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
            var button = new Button
            {
                Content = new TextBlock { Text = glyph, FontFamily = Glyphs.Font, FontSize = 12, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Width = 28, Height = 28, Margin = new Thickness(6, 0, 0, 0), Background = normal, BorderThickness = new Thickness(0),
                Template = Controls.RoundTemplate(14), Cursor = Cursors.Hand, ToolTip = help, Focusable = false,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, help);
            button.MouseEnter += (_, _) => button.Background = hover;
            button.MouseLeave += (_, _) => button.Background = normal;
            button.Click += (_, _) => click();
            return button;
        }
    }

    /// <summary>A dashed line just outside the recorded area; clicks go through it.</summary>
    private sealed class AreaFrame : OverlayWindow
    {
        public AreaFrame(double scale) : base(activating: false, transparent: true, clickThrough: true)
        {
            Content = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(255, 69, 58)), StrokeThickness = 1.5, StrokeDashArray = [4, 3],
                Margin = new Thickness(1 / scale),
            };
        }
    }
}
