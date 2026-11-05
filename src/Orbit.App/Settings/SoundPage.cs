using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Orbit.App.Design;
using Orbit.Core;
using Orbit.Core.Audio;

namespace Orbit.App.Settings;

/// <summary>
/// The Sound page (Orbit: the volume and mute parts of the Sound page): the output's volume, and
/// each program with sound open, with its own volume, mute and a live level. Windows remembers the
/// levels; the page only changes them. The list is read again every 1.5 s, the levels 10 times a
/// second, and only while the page shows.
/// </summary>
internal sealed class SoundPage : StackPanel
{
    private readonly AudioMixer _mixer = new();
    private readonly Palette _palette = Palette.Light;
    private readonly PageParts _parts = new(Palette.Light);
    private readonly StackPanel _master = new();
    private readonly StackPanel _apps = new();
    private readonly Dictionary<string, (Slider Volume, Button Mute, Border Level)> _rows = [];
    private readonly DispatcherTimer _list = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly DispatcherTimer _meter = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private string _keys = "\0";
    /// <summary>Set while the page moves a control itself: that is not the user changing a volume.</summary>
    private bool _updating;

    public SoundPage()
    {
        var header = _parts.PageTitles(Strings.PageSound, Strings.SoundSubtitle, bottom: 20);
        Children.Add(header);
        Children.Add(_parts.Group(null, null, _master));
        Children.Add(_parts.Group(Strings.SoundApps, Strings.SoundFooter, _apps));
        _list.Tick += (_, _) => Refresh();
        _meter.Tick += (_, _) => Meter();
        Loaded += (_, _) =>
        {
            _list.Start();
            _meter.Start();
        };
        Unloaded += (_, _) =>
        {
            _list.Stop();
            _meter.Stop();
        };
        FillMaster();
        Refresh();
    }

    private void FillMaster()
    {
        _master.Children.Clear();
        _masterRow = null;
        var current = Safe(() => _mixer.Master());
        _hadOutput = current is not null;
        if (current is not { } master)
        {
            _master.Children.Add(_parts.Row(Strings.SoundNoOutput, null, new Border()));
            return;
        }
        var row = Controls(master.Volume, master.Muted, v => Safe(() => _mixer.SetMaster(v)), m => Safe(() => _mixer.SetMasterMuted(m)), out var parts);
        _masterRow = parts;
        _master.Children.Add(_parts.Row(Strings.SoundOutput, null, row));
    }

    /// <summary>Rebuilds the rows when programs came or went; otherwise moves the controls the user isn't holding.</summary>
    private void Refresh()
    {
        RefreshMaster();
        if (Safe(() => _mixer.Apps()) is not { } apps) return;
        var keys = string.Join("|", apps.Select(a => a.Key));
        if (keys != _keys)
        {
            _keys = keys;
            _rows.Clear();
            _apps.Children.Clear();
            if (apps.Count == 0) _apps.Children.Add(_parts.Caption(Strings.SoundNoApps));
            for (int i = 0; i < apps.Count; i++)
            {
                if (i > 0) _apps.Children.Add(new Border { Height = 1, Background = _palette.Hairline, Margin = new Thickness(0, 10, 0, 10) });
                var app = apps[i];
                var key = app.Key;
                var controls = Controls(app.Volume, app.Muted, v => Safe(() => _mixer.SetVolume(key, v)), m => Safe(() => _mixer.SetMuted(key, m)), out var parts);
                _rows[key] = parts;
                var name = app.IsSystemSounds ? Strings.SoundSystem : app.Name;
                _apps.Children.Add(_parts.Row(name, null, controls));
            }
            return;
        }
        foreach (var app in apps)
            if (_rows.TryGetValue(app.Key, out var row)) Show(row, app.Volume, app.Muted);
    }

    private (Slider Volume, Button Mute, Border Level)? _masterRow;
    private bool _hadOutput;

    /// <summary>The output's own level, changed elsewhere (keyboard keys, the taskbar) or a device that came or went.</summary>
    private void RefreshMaster()
    {
        var master = Safe(() => _mixer.Master());
        if ((master is not null) != _hadOutput)
        {
            FillMaster();
            return;
        }
        if (master is { } m && _masterRow is { } row) Show(row, m.Volume, m.Muted);
    }

    /// <summary>The control follows Windows, except while the user holds it.</summary>
    private void Show((Slider Volume, Button Mute, Border Level) row, float volume, bool muted)
    {
        if (row.Volume.IsMouseCaptureWithin) return;
        _updating = true;
        try
        {
            row.Volume.Value = Math.Round(volume * 100);
        }
        finally
        {
            _updating = false;
        }
        Paint(row.Mute, muted);
    }

    private void Meter()
    {
        if (_rows.Count == 0 || Safe(() => _mixer.Peaks()) is not { } peaks) return;
        foreach (var (key, row) in _rows) row.Level.Width = 120 * Math.Clamp(peaks.GetValueOrDefault(key), 0, 1);
    }

    /// <summary>A volume slider with its percentage, a mute button and a level bar under the slider.</summary>
    private FrameworkElement Controls(float volume, bool muted, Action<float> setVolume, Action<bool> setMuted, out (Slider Volume, Button Mute, Border Level) parts)
    {
        var percent = new TextBlock { Width = 40, FontSize = 12, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = Math.Round(volume * 100), Width = 120, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
        percent.Text = $"{slider.Value:0}%";
        slider.ValueChanged += (_, e) =>
        {
            percent.Text = $"{e.NewValue:0}%";
            if (!_updating) setVolume((float)(e.NewValue / 100));
        };
        var level = new Border { Height = 3, Width = 0, Background = _palette.Accent, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left };
        var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        column.Children.Add(slider);
        column.Children.Add(new Border { Width = 120, Height = 3, Background = _palette.Raised, CornerRadius = new CornerRadius(1.5), Child = level, Margin = new Thickness(0, 2, 0, 0) });
        var mute = new Button
        {
            Width = 30, Height = 30, BorderThickness = new Thickness(0), Template = Design.Controls.RoundTemplate(15), Cursor = Cursors.Hand,
            ToolTip = Strings.SoundMute, Margin = new Thickness(10, 0, 0, 0),
        };
        bool isMuted = muted;
        Paint(mute, isMuted);
        mute.Click += (_, _) =>
        {
            isMuted = mute.Tag is not true;
            Paint(mute, isMuted);
            setMuted(isMuted);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(column);
        row.Children.Add(percent);
        row.Children.Add(mute);
        parts = (slider, mute, level);
        return row;
    }

    private void Paint(Button mute, bool muted)
    {
        mute.Tag = muted;
        mute.Background = muted ? _palette.Ink : _palette.Raised;
        mute.Content = new TextBlock { Text = muted ? "" : "", FontFamily = Glyphs.Font, FontSize = 13, Foreground = muted ? _palette.Canvas : _palette.Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Audio devices come and go; a failed call shows as nothing rather than an error.</summary>
    private static T? Safe<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidCastException or UnauthorizedAccessException)
        {
            Log.Warn($"Audio call failed ({e.GetType().Name})");
            return default;
        }
    }

    private static void Safe(Action change) => Safe(() =>
    {
        change();
        return true;
    });
}
