using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Orbit.App.Overlays;
using Orbit.Core.Capture.Recording;
using Orbit.Core.Display;

namespace Orbit.App.Features.Capture;

/// <summary>
/// A ring where the mouse was pressed while recording (Orbit: ClickHighlighter.swift): an orange
/// disc, blue for the right button, that grows and fades. It is a small click-through window that
/// the recording keeps in. A recording of one window can't hold a second window, so those have no
/// rings (Orbit paints them into the frames).
/// </summary>
internal sealed class ClickRing : OverlayWindow
{
    private const int MostAtOnce = 8;
    private static readonly Duration Life = new(TimeSpan.FromSeconds(0.55));
    private static int _showing;

    private ClickRing(bool secondary) : base(activating: false, transparent: true, clickThrough: true)
    {
        var tint = secondary ? Color.FromRgb(10, 132, 255) : Color.FromRgb(255, 107, 26);
        var ring = new Grid { Width = 52, Height = 52, IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5) };
        ring.Children.Add(new Ellipse
        {
            Width = 46, Height = 46, StrokeThickness = 2.5,
            Fill = new SolidColorBrush(Color.FromArgb(82, tint.R, tint.G, tint.B)),
            Stroke = new SolidColorBrush(Color.FromArgb(242, tint.R, tint.G, tint.B)),
        });
        ring.Children.Add(new Ellipse { Width = 51, Height = 51, StrokeThickness = 1, Stroke = new SolidColorBrush(Color.FromArgb(217, 255, 255, 255)) });
        var grow = new ScaleTransform();
        ring.RenderTransform = grow;
        Content = ring;

        var scale = new DoubleAnimation(0.45, 1.15, Life) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var fade = new DoubleAnimationUsingKeyFrames { Duration = Life };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.45)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        fade.Completed += (_, _) => Close();
        Closed += (_, _) => _showing--;
        grow.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        grow.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        ring.BeginAnimation(OpacityProperty, fade);
    }

    /// <param name="x">The press, in physical pixels.</param>
    /// <param name="y">The press, in physical pixels.</param>
    /// <param name="secondary">The right button.</param>
    /// <param name="scale">The monitor's scale.</param>
    public static void Pulse(int x, int y, bool secondary, double scale)
    {
        if (_showing >= MostAtOnce) return;
        _showing++;
        new ClickRing(secondary).ShowAt(RecordingExtras.RingPlace(x, y, scale));
    }
}

/// <summary>3, 2, 1 over the middle of what is about to be recorded (Orbit: Countdown). Stays out of the video.</summary>
internal sealed class RecordCountdown : OverlayWindow
{
    private readonly TextBlock _number;
    private bool _cancelled;

    private RecordCountdown() : base(activating: false, transparent: true, clickThrough: true)
    {
        _number = new TextBlock { FontSize = 92, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
        var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        column.Children.Add(_number);
        column.Children.Add(new TextBlock
        {
            Text = Strings.RecordCountdownCancel, FontSize = 12, FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(153, 255, 255, 255)),
        });
        Content = new Border
        {
            Width = 180, Height = 180, CornerRadius = new CornerRadius(90), Child = column,
            Background = new SolidColorBrush(Color.FromArgb(158, 0, 0, 0)), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
        };
    }

    public static RecordCountdown Show(MonitorInfo monitor, PixelRect target)
    {
        var countdown = new RecordCountdown();
        countdown.ShowAt(RecordingExtras.CountdownPlace(target, monitor.WorkArea, monitor.Scale));
        countdown.ExcludeFromCapture();
        return countdown;
    }

    public void Cancel() => _cancelled = true;

    /// <summary>Counts down and closes. False when it was cancelled.</summary>
    public async Task<bool> RunAsync(int seconds)
    {
        for (int left = seconds; left >= 1 && !_cancelled; left--)
        {
            _number.Text = left.ToString();
            for (int tick = 0; tick < 10 && !_cancelled; tick++) await Task.Delay(100);
        }
        Close();
        // The screen drops the countdown before the first frame is taken.
        await Task.Delay(80);
        return !_cancelled;
    }
}
