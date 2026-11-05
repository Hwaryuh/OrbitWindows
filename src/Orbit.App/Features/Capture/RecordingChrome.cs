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
/// the app-sound and microphone switches, stop and discard, and a dashed frame around a recorded
/// area. Both stay out of the video.
/// </summary>
internal sealed class RecordingChrome
{
    private const double BaseWidth = 168, ButtonWidth = 34, BarHeight = 40, Gap = 12;

    /// <summary>The recording red (Orbit's): the dot, the area's frame and a source that is off.</summary>
    private static readonly Brush RecordRed = new SolidColorBrush(Color.FromRgb(255, 69, 58)).Frozen();

    private readonly ControlBar _bar;
    private readonly AreaFrame? _frame;

    private RecordingChrome(ControlBar bar, AreaFrame? frame)
    {
        _bar = bar;
        _frame = frame;
    }

    /// <param name="area">What is recorded, in physical pixels; null for a window (it may move).</param>
    /// <param name="anchor">Where the bar goes: below this rectangle, or above it, or inside at the bottom.</param>
    /// <param name="sound">The sound switches: app sound always, the microphone when <see cref="SoundSwitches.HasMic"/>.</param>
    /// <param name="clicks">The click rings' switch; null when this recording can't have rings.</param>
    public static RecordingChrome Show(MonitorInfo monitor, PixelRect anchor, PixelRect? area, Action stop, Action discard, SoundSwitches sound, ClickSwitch? clicks)
    {
        var bar = new ControlBar(stop, discard, sound, clicks);
        bar.ShowAt(BarPlace(monitor, anchor, bar.BarWidth));
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

    /// <summary>Whether a point in physical pixels is on the bar.</summary>
    public bool BarContains(int x, int y) => _bar.Placement.Contains(x, y);

    /// <summary>The switches show the state again (after the microphone could not be opened).</summary>
    public void Refresh() => _bar.Refresh();

    /// <summary>Below the anchor when there is room on its monitor, else above it, else inside at the bottom.</summary>
    internal static PixelRect BarPlace(MonitorInfo monitor, PixelRect anchor, double barWidth = BaseWidth)
    {
        double scale = monitor.Scale;
        int width = (int)Math.Round(barWidth * scale), height = (int)Math.Round(BarHeight * scale), gap = (int)Math.Round(Gap * scale);
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
        private readonly SoundSwitches _sound;
        private readonly Button _app;
        private readonly Button? _mic;
        private readonly ClickSwitch? _clicks;
        private readonly Button? _clicksButton;

        public double BarWidth { get; }

        public ControlBar(Action stop, Action discard, SoundSwitches sound, ClickSwitch? clicks) : base(activating: false, transparent: true, clickThrough: false)
        {
            _sound = sound;
            _clicks = clicks;
            BarWidth = BaseWidth + ButtonWidth * (1 + (sound.HasMic() ? 1 : 0) + (clicks is null ? 0 : 1));
            _time = new TextBlock
            {
                Text = CaptureCard.Clock(TimeSpan.Zero), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), MinWidth = 44,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 8, 0) };
            row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = RecordRed, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(_time);
            _app = Button(Glyphs.Volume, Strings.RecordAppSoundOff, () =>
            {
                sound.SetApp(!sound.App());
                Refresh();
            });
            row.Children.Add(_app);
            if (sound.HasMic())
            {
                _mic = Button(Glyphs.Microphone, Strings.RecordMicOff, () =>
                {
                    sound.SetMic(!sound.Mic());
                    Refresh();
                });
                row.Children.Add(_mic);
            }
            if (clicks is not null)
            {
                _clicksButton = Button(Glyphs.Pointer, Strings.RecordClicksOff, () =>
                {
                    clicks.Set(!clicks.Get());
                    Refresh();
                });
                row.Children.Add(_clicksButton);
            }
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
            Refresh();
        }

        /// <summary>Each switch shows what it would do; a source that is off is drawn in red, as Orbit's accent.</summary>
        public void Refresh()
        {
            Show(_app, _sound.App(), Glyphs.Volume, Glyphs.Mute, Strings.RecordAppSoundOff, Strings.RecordAppSoundOn);
            // A microphone that could not be opened stays as a switch: pressing it again tries again.
            if (_mic is not null) Show(_mic, _sound.Mic(), Glyphs.Microphone, Glyphs.MicOff, Strings.RecordMicOff, Strings.RecordMicOn);
            if (_clicksButton is not null) Show(_clicksButton, _clicks!.Get(), Glyphs.Pointer, Glyphs.Pointer, Strings.RecordClicksOff, Strings.RecordClicksOn);
        }

        private static void Show(Button button, bool on, string onGlyph, string offGlyph, string onHelp, string offHelp)
        {
            var text = (TextBlock)button.Content;
            text.Text = on ? onGlyph : offGlyph;
            text.Foreground = on ? Brushes.White : RecordRed;
            string help = on ? onHelp : offHelp;
            button.ToolTip = help;
            System.Windows.Automation.AutomationProperties.SetName(button, help);
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
                Stroke = RecordRed, StrokeThickness = 1.5, StrokeDashArray = [4, 3],
                Margin = new Thickness(1 / scale),
            };
        }
    }
}

/// <summary>What the bar's sound switches read and change.</summary>
internal sealed record SoundSwitches(Func<bool> App, Action<bool> SetApp, Func<bool> Mic, Action<bool> SetMic, Func<bool> HasMic);

/// <summary>What the bar's click rings switch reads and changes.</summary>
internal sealed record ClickSwitch(Func<bool> Get, Action<bool> Set);
