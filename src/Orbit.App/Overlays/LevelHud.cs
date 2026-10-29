using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Orbit.App.Design;
using Orbit.Core.Display;

namespace Orbit.App.Overlays;

/// <summary>
/// The brightness bar (Orbit: Modules/Displays/DisplayOSD.swift): a sun, a bar and the
/// percentage at the bottom of the screen that changed. Never takes focus or clicks; repeated
/// changes update the one bar, which goes 1.5 s after the last.
/// </summary>
public sealed class LevelHud : OverlayWindow
{
    private const double BottomDip = 96, BarWidth = 180;

    private static LevelHud? _current;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly Border _body;
    private readonly Border _fill;
    private readonly TextBlock _percent;
    private nint _monitor;

    private LevelHud() : base(activating: false, transparent: true, clickThrough: true)
    {
        _fill = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
        var track = new Border
        {
            Width = BarWidth, Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center, Child = _fill,
        };
        _percent = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "", FontFamily = Glyphs.Font, FontSize = 16, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        row.Children.Add(track);
        row.Children.Add(_percent);
        _body = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 20, 20, 20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(18, 11, 18, 11),
            Child = row,
        };
        Content = _body;
        _timer.Tick += (_, _) => FadeOut();
    }

    public static void Show(nint monitor, double level)
    {
        var screen = Monitors.All().FirstOrDefault(m => m.Handle == monitor);
        if (screen is null) return;
        if (_current is null || _current._monitor != monitor)
        {
            _current?.Close();
            var hud = new LevelHud { _monitor = monitor };
            hud.Closed += (_, _) => { if (_current == hud) _current = null; };
            _current = hud;
            hud.Set(level);
            hud._body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = hud._body.DesiredSize;
            int width = (int)Math.Ceiling(size.Width * screen.Scale), height = (int)Math.Ceiling(size.Height * screen.Scale);
            var work = screen.WorkArea;
            hud.ShowAt(new PixelRect(work.X + (work.Width - width) / 2, work.Bottom - height - (int)Math.Round(BottomDip * screen.Scale), width, height));
        }
        else
        {
            _current.BeginAnimation(OpacityProperty, null);
            _current.Opacity = 1;
            _current.Set(level);
        }
        _current._timer.Stop();
        _current._timer.Start();
    }

    private void Set(double level)
    {
        level = Math.Clamp(level, 0, 1);
        _fill.Width = BarWidth * level;
        _percent.Text = $"{Math.Round(level * 100)}%";
    }

    private void FadeOut()
    {
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            // A change during the fade brought it back.
            if (Opacity == 0) Close();
        };
        BeginAnimation(OpacityProperty, fade);
    }
}
