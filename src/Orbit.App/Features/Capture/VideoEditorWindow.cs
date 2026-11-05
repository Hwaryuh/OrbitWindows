using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Orbit.App.Design;
using Orbit.App.Overlays;
using Orbit.App.Settings;
using Orbit.Core;
using Orbit.Core.Capture;
using Orbit.Core.Capture.Recording;
using Windows.Win32;
using Windows.Win32.Graphics.Dwm;

namespace Orbit.App.Features.Capture;

/// <summary>
/// Trim, mute and export a recording (Orbit: VideoEditor.swift): the player on top, a filmstrip with
/// trim handles below. The original is never changed. Nothing plays until asked. Keys: Space plays,
/// arrows step a frame (Shift: a second), I and O set the ends, M mutes, Ctrl+S saves (Shift asks
/// where), Ctrl+C copies, Esc closes.
/// </summary>
internal sealed class VideoEditorWindow : Window
{
    private const int FilmstripPictures = 14, FilmstripSide = 240;

    private static readonly List<VideoEditorWindow> Open = [];
    private static readonly string ExportFolder = Path.Combine(Path.GetTempPath(), "Orbit", "Edited");

    private readonly string _path;
    private readonly CaptureActions _actions;
    private readonly Palette _palette = Palette.DarkTheme;
    private readonly MediaElement _player = new()
    {
        LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, ScrubbingEnabled = true, Stretch = Stretch.Uniform,
    };
    private readonly VideoTimeline _timeline;
    private readonly TextBlock _clock = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock _status = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _working = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    private readonly Border _workingCard;
    private readonly Button _play, _mute, _reset;
    private readonly TextBlock _playGlyph = new() { FontFamily = Glyphs.Font, FontSize = 13 };
    private readonly TextBlock _muteGlyph = new() { FontFamily = Glyphs.Font, FontSize = 13 };
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly CancellationTokenSource _closing = new();
    private VideoTrim? _trim;
    private bool _muted;
    private bool _playing;
    private bool _hasSound;
    private bool _busy;
    private bool _opened;
    private int _fps = 30;
    private string? _exported;

    private VideoEditorWindow(string path, CaptureActions actions)
    {
        _path = path;
        _actions = actions;
        Title = Path.GetFileNameWithoutExtension(path);
        Background = _palette.Canvas;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Malgun Gothic");
        Foreground = _palette.Ink;
        MinWidth = 680;
        MinHeight = 460;
        ShowInTaskbar = true;

        var parts = new PageParts(_palette);
        _mute = IconButton(_muteGlyph, Strings.VideoSoundOff, ToggleMute);
        var resetGlyph = new TextBlock { FontFamily = Glyphs.Font, FontSize = 13, Text = "" };
        _reset = IconButton(resetGlyph, Strings.VideoUndoEdits, ResetEdits);
        var copy = parts.PillButton(Strings.ActionCopy, prominent: false, () => _ = CopyAsync());
        copy.Margin = new Thickness(8, 0, 0, 0);
        copy.ToolTip = Strings.VideoCopyHint;
        var save = parts.PillButton(Strings.VideoSave, prominent: true, () => { });
        save.Margin = new Thickness(8, 0, 0, 0);
        save.ToolTip = Strings.VideoSaveHint;
        save.Click += (_, _) =>
        {
            save.ContextMenu = SaveMenu();
            save.ContextMenu.PlacementTarget = save;
            save.ContextMenu.IsOpen = true;
        };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        tools.Children.Add(_mute);
        tools.Children.Add(_reset);
        tools.Children.Add(copy);
        tools.Children.Add(save);
        var toolbar = new DockPanel { Margin = new Thickness(20, 12, 20, 12) };
        DockPanel.SetDock(tools, Dock.Right);
        toolbar.Children.Add(tools);
        toolbar.Children.Add(new TextBlock { Text = Title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = _palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });

        var surface = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(10), ClipToBounds = true, Margin = new Thickness(20, 0, 20, 0), Child = _player };
        surface.MouseLeftButtonUp += (_, _) => TogglePlay();

        _play = IconButton(_playGlyph, Strings.VideoPlay, TogglePlay);
        _play.Margin = new Thickness(0);
        _clock.Foreground = _palette.InkSecondary;
        _status.Foreground = _palette.InkSecondary;
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        row.Children.Add(_play);
        row.Children.Add(_clock);
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        row.Children.Add(_status);
        _timeline = new VideoTimeline(_palette.Accent) { Height = 56 };
        _timeline.SeekAsked += SeekTo;
        _timeline.StartAsked += seconds => SetEnd(start: true, seconds);
        _timeline.EndAsked += seconds => SetEnd(start: false, seconds);
        var controls = new StackPanel { Margin = new Thickness(20, 14, 20, 18) };
        controls.Children.Add(row);
        controls.Children.Add(_timeline);

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        DockPanel.SetDock(controls, Dock.Bottom);
        layout.Children.Add(controls);
        layout.Children.Add(surface);

        _workingCard = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 28, 28, 30)), CornerRadius = new CornerRadius(17), Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = _working, Visibility = Visibility.Collapsed,
        };
        var root = new Grid();
        root.Children.Add(layout);
        root.Children.Add(_workingCard);
        Content = root;

        _player.MediaOpened += (_, _) =>
        {
            _opened = true;
            _player.Pause();
        };
        _player.MediaFailed += (_, e) =>
        {
            Log.Warn($"A recording could not be played ({e.ErrorException?.GetType().Name})");
            Toast.Show(Strings.VideoCantPlay);
        };
        _tick.Tick += (_, _) => OnTick();
        PreviewKeyDown += OnKey;
        SourceInitialized += (_, _) => TitleBars.MakeDark(this);
        Closed += (_, _) => Cleanup();
        Refresh();
    }

    /// <summary>Opens the recording, or brings up the window that already has it.</summary>
    public static void OpenFor(string path, CaptureActions actions)
    {
        if (!File.Exists(path))
        {
            Toast.Show(Strings.CaptureFileMissing);
            return;
        }
        if (Open.FirstOrDefault(w => string.Equals(w._path, path, StringComparison.OrdinalIgnoreCase)) is { } open)
        {
            open.Activate();
            return;
        }
        var window = new VideoEditorWindow(path, actions);
        Open.Add(window);
        var place = WindowPlacement.PointerMonitor() is { } monitor ? WindowPlacement.Centered(monitor, 1000, 700) : default;
        if (place.IsEmpty)
        {
            window.Width = 1000;
            window.Height = 700;
            window.Show();
        }
        else WindowPlacement.ShowAt(window, place);
        window.Activate();
        _ = window.LoadAsync();
    }

    /// <summary>Exports left from an earlier run: a pasted file has had its chance by now.</summary>
    public static void ClearOldExports()
    {
        try
        {
            if (Directory.Exists(ExportFolder)) Directory.Delete(ExportFolder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Old video exports could not be cleared ({e.GetType().Name})");
        }
    }

    public static string ExportsFolder => ExportFolder;

    /// <summary>A window not yet shown or loaded, for <see cref="VideoEditorSelfTest"/> to place itself.</summary>
    internal static VideoEditorWindow ForSelfTest(string path, CaptureActions actions) => new(path, actions);

    internal VideoTrim? Trim => _trim;
    internal bool HasFilmstrip => _timeline.HasPictures;
    internal bool IsPlaying => _playing;
    internal Task SaveForTest(bool gif) => SaveAsync(gif, ask: false);
    internal void SetEndForTest(bool start, TimeSpan at) => SetEnd(start, at);

    internal async Task LoadAsync()
    {
        VideoInfo info;
        try
        {
            info = await Task.Run(() => VideoProbe.Read(_path));
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"A recording could not be read (0x{e.HResult:X8})");
            Toast.Show(Strings.VideoCantPlay);
            Close();
            return;
        }
        if (_closing.IsCancellationRequested) return;
        if (info.Length <= TimeSpan.Zero)
        {
            Toast.Show(Strings.VideoCantPlay);
            Close();
            return;
        }
        _trim = new VideoTrim(info.Length);
        _hasSound = info.Sound is not null;
        _fps = info.Frames > 0 ? Math.Clamp((int)Math.Round(info.Frames / info.Length.TotalSeconds), 1, 120) : 30;
        _timeline.Length = info.Length;
        _player.Source = new Uri(_path);
        Refresh();
        _ = LoadFilmstripAsync(info.Length);
    }

    private async Task LoadFilmstripAsync(TimeSpan length)
    {
        try
        {
            var pictures = await VideoExport.FilmstripAsync(_path, length, FilmstripPictures, FilmstripSide, _closing.Token);
            _timeline.Pictures = pictures.Select(p =>
            {
                var bitmap = BitmapSource.Create(p.Width, p.Height, 96, 96, PixelFormats.Bgr32, null, p.Pixels, p.Width * 4);
                bitmap.Freeze();
                return bitmap;
            }).ToList();
        }
        catch (OperationCanceledException)
        {
            // The window closed.
        }
        catch (Exception e) when (e is COMException or InvalidOperationException)
        {
            // The strip stays empty; trimming still works.
            Log.Warn($"A recording's filmstrip could not be read ({e.GetType().Name})");
        }
    }

    private void Cleanup()
    {
        Open.Remove(this);
        _closing.Cancel();
        _tick.Stop();
        _player.Stop();
        // Lets go of the file, so it can be moved or thrown away.
        _player.Close();
        _player.Source = null;
    }

    private Button IconButton(TextBlock glyph, string help, Action click)
    {
        glyph.Foreground = _palette.Ink;
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var button = new Button
        {
            Content = glyph, Width = 32, Height = 32, Margin = new Thickness(8, 0, 0, 0), Background = _palette.Raised, BorderThickness = new Thickness(0),
            Template = Controls.RoundTemplate(16), Cursor = Cursors.Hand, ToolTip = help, Focusable = false,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, help);
        button.Click += (_, _) => click();
        return button;
    }

    private ContextMenu SaveMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Controls.MenuItem(Strings.VideoSaveMp4, () => _ = SaveAsync(gif: false, ask: false)));
        menu.Items.Add(Controls.MenuItem(Strings.VideoSaveMp4Ask, () => _ = SaveAsync(gif: false, ask: true)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Controls.MenuItem(Strings.VideoSaveGif, () => _ = SaveAsync(gif: true, ask: false)));
        menu.Items.Add(Controls.MenuItem(Strings.VideoSaveGifAsk, () => _ = SaveAsync(gif: true, ask: true)));
        return menu;
    }

    /// <summary>Where the playhead is; before the file has opened, the start.</summary>
    private TimeSpan Now => _opened ? _player.Position : TimeSpan.Zero;

    private void Refresh()
    {
        _playGlyph.Text = _playing ? "" : "";
        _play.ToolTip = _playing ? Strings.VideoPause : Strings.VideoPlay;
        _mute.Visibility = _hasSound ? Visibility.Visible : Visibility.Collapsed;
        _muteGlyph.Text = _muted ? Glyphs.Mute : Glyphs.Volume;
        _muteGlyph.Foreground = _muted ? _palette.Accent : _palette.Ink;
        _mute.ToolTip = _muted ? Strings.VideoSoundOn : Strings.VideoSoundOff;
        _player.IsMuted = _muted;
        bool edited = _trim?.IsEdited(_muted) == true;
        _reset.IsEnabled = edited;
        _reset.Opacity = edited ? 1 : 0.4;
        if (_trim is { } trim)
        {
            _clock.Text = $"{VideoTrim.Clock(Now)} / {VideoTrim.Clock(trim.Length)}";
            _status.Text = edited ? Strings.VideoKeeping(VideoTrim.Clock(trim.End - trim.Start)) : Strings.VideoDragToTrim;
            _status.Foreground = edited ? _palette.Accent : _palette.InkSecondary;
            _timeline.Start = trim.Start;
            _timeline.End = trim.End;
            _timeline.Current = Now;
        }
    }

    private void OnTick()
    {
        if (_trim is { } trim && _playing && Now >= trim.End)
        {
            Pause();
            SeekTo(trim.End);
        }
        Refresh();
    }

    private void TogglePlay()
    {
        if (_trim is not { } trim || !_opened) return;
        if (_playing)
        {
            Pause();
            return;
        }
        var at = Now;
        if (at >= trim.End - TimeSpan.FromSeconds(0.05) || at < trim.Start) _player.Position = trim.Start;
        _player.Play();
        _playing = true;
        _tick.Start();
        Refresh();
    }

    private void Pause()
    {
        _player.Pause();
        _playing = false;
        _tick.Stop();
        Refresh();
    }

    private void SeekTo(TimeSpan at)
    {
        if (_trim is not { } trim) return;
        _player.Position = trim.Clamped(at);
        Refresh();
    }

    /// <summary>Edits wait while an export runs, so what it writes is what the window shows.</summary>
    private void SetEnd(bool start, TimeSpan at)
    {
        if (_trim is not { } trim || _busy) return;
        if (start) trim.SetStart(at);
        else trim.SetEnd(at);
        _exported = null;
        _player.Position = start ? trim.Start : trim.End;
        Refresh();
    }

    private void ToggleMute()
    {
        if (!_hasSound || _busy) return;
        _muted = !_muted;
        _exported = null;
        Refresh();
    }

    private void ResetEdits()
    {
        if (_busy) return;
        _trim?.Reset();
        _muted = false;
        _exported = null;
        Refresh();
    }

    private async Task CopyAsync()
    {
        if (await RenderAsync(gif: false) is { } file) CaptureActions.CopyVideo(file);
    }

    private async Task SaveAsync(bool gif, bool ask)
    {
        if (await RenderAsync(gif) is not { } file) return;
        try
        {
            if (ask)
            {
                var dialog = new SaveFileDialog
                {
                    FileName = Path.GetFileName(file), Filter = gif ? "GIF|*.gif" : "MP4|*.mp4", DefaultExt = gif ? ".gif" : ".mp4",
                    InitialDirectory = _actions.Store.SaveFolder, OverwritePrompt = true,
                };
                if (dialog.ShowDialog(this) != true) return;
                // Saving the original over itself would lose it.
                if (!string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase)) File.Copy(file, dialog.FileName, overwrite: true);
                Toast.Show(Strings.CaptureSaved, Path.GetFileName(dialog.FileName));
                return;
            }
            var saved = _actions.Store.SaveCopy(file);
            Toast.Show(Strings.CaptureSaved, Path.GetFileName(saved));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warn($"An exported video could not be saved ({e.GetType().Name})");
            Toast.Show(Strings.CaptureSaveFailed);
        }
    }

    /// <summary>The clip as a file: the original when nothing changed, else an export kept until the next edit. Null when it failed.</summary>
    private async Task<string?> RenderAsync(bool gif)
    {
        if (_trim is not { } trim || _busy) return null;
        bool edited = trim.IsEdited(_muted);
        if (!gif && !edited) return _path;
        if (!gif && _exported is { } ready && File.Exists(ready)) return ready;
        if (_playing) Pause();
        _busy = true;
        _working.Text = gif ? Strings.VideoMakingGif : Strings.VideoExporting;
        _workingCard.Visibility = Visibility.Visible;
        try
        {
            var folder = Path.Combine(ExportFolder, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var name = Path.GetFileNameWithoutExtension(_path) + (edited ? " (Edited)" : "");
            var output = Path.Combine(folder, name + (gif ? ".gif" : ".mp4"));
            var part = new KeptPart(trim.Start, trim.End);
            if (gif) await VideoExport.ExportGifAsync(_path, output, part, _closing.Token);
            else
            {
                await VideoExport.ExportMp4Async(_path, output, part, _muted, _closing.Token);
                _exported = output;
            }
            return output;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Log.Warn($"A video could not be exported ({e.GetType().Name} 0x{e.HResult:X8})");
            Toast.Show(gif ? Strings.VideoGifFailed : Strings.VideoExportFailed);
            return null;
        }
        finally
        {
            _busy = false;
            _workingCard.Visibility = Visibility.Collapsed;
        }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var step = shift ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(1.0 / _fps);
        e.Handled = true;
        switch (key)
        {
            case Key.S when ctrl: _ = SaveAsync(gif: false, ask: shift); break;
            case Key.C when ctrl: _ = CopyAsync(); break;
            case Key.Space when !ctrl: TogglePlay(); break;
            case Key.Left when !ctrl: SeekTo(Now - step); break;
            case Key.Right when !ctrl: SeekTo(Now + step); break;
            case Key.I when !ctrl: SetEnd(start: true, Now); break;
            case Key.O when !ctrl: SetEnd(start: false, Now); break;
            case Key.M when !ctrl: ToggleMute(); break;
            case Key.Escape: Close(); break;
            default: e.Handled = false; break;
        }
    }

}

/// <summary>The filmstrip: drag the two handles to trim, anywhere else to move the playhead.</summary>
internal sealed class VideoTimeline(Brush accent) : FrameworkElement
{
    private const double Handle = 12, Reach = 16;
    private static readonly Brush Dim = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)).Frozen();
    private static readonly Brush Empty = new SolidColorBrush(Color.FromRgb(58, 58, 60)).Frozen();
    private readonly Pen _edge = new Pen(accent, 3).Frozen();

    private enum Grab { None, Start, End, Playhead }

    private Grab _grab;
    private TimeSpan _length, _start, _end, _current;
    private IReadOnlyList<BitmapSource> _pictures = [];

    public event Action<TimeSpan>? SeekAsked, StartAsked, EndAsked;

    public TimeSpan Length { get => _length; set => Set(ref _length, value); }
    public TimeSpan Start { get => _start; set => Set(ref _start, value); }
    public TimeSpan End { get => _end; set => Set(ref _end, value); }
    public TimeSpan Current { get => _current; set => Set(ref _current, value); }

    public bool HasPictures => _pictures.Count > 0;

    public IReadOnlyList<BitmapSource> Pictures
    {
        set
        {
            _pictures = value;
            InvalidateVisual();
        }
    }

    private void Set(ref TimeSpan field, TimeSpan value)
    {
        if (field == value) return;
        field = value;
        InvalidateVisual();
    }

    private double Inner => Math.Max(1, ActualWidth - Handle * 2);

    private double X(TimeSpan at) => Handle + (_length > TimeSpan.Zero ? at / _length : 0) * Inner;

    private TimeSpan TimeAt(double x) => _length * Math.Clamp((x - Handle) / Inner, 0, 1);

    protected override void OnRender(DrawingContext dc)
    {
        double height = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, height));
        var strip = new Rect(Handle, 0, Inner, height);
        dc.PushClip(new RectangleGeometry(strip, 6, 6));
        dc.DrawRectangle(Empty, null, strip);
        if (_pictures.Count > 0)
        {
            double each = Inner / _pictures.Count;
            for (int i = 0; i < _pictures.Count; i++)
            {
                // Fills its cell in proportion, cut at the sides.
                var picture = _pictures[i];
                double scale = Math.Max(each / picture.PixelWidth, height / picture.PixelHeight);
                double width = picture.PixelWidth * scale, tall = picture.PixelHeight * scale;
                var cell = new Rect(Handle + i * each, 0, each + 0.5, height);
                dc.PushClip(new RectangleGeometry(cell));
                dc.DrawImage(picture, new Rect(cell.X + (each - width) / 2, (height - tall) / 2, width, tall));
                dc.Pop();
            }
        }
        dc.Pop();

        double from = X(_start), to = X(_end);
        dc.DrawRectangle(Dim, null, new Rect(Handle, 0, Math.Max(0, from - Handle), height));
        dc.DrawRectangle(Dim, null, new Rect(to, 0, Math.Max(0, Handle + Inner - to), height));
        dc.DrawRoundedRectangle(null, _edge, new Rect(from - Handle + 1.5, 1.5, Math.Max(0, to - from + Handle * 2 - 3), height - 3), 7, 7);
        DrawGrip(dc, from - Handle, height);
        DrawGrip(dc, to, height);

        double head = X(_current);
        dc.DrawRoundedRectangle(Brushes.White, null, new Rect(head - 1.5, -4, 3, height + 8), 1.5, 1.5);
    }

    private void DrawGrip(DrawingContext dc, double x, double height)
    {
        dc.DrawRoundedRectangle(accent, null, new Rect(x, 0, Handle, height), 4, 4);
        dc.DrawRoundedRectangle(Brushes.White, null, new Rect(x + Handle / 2 - 1, height / 2 - 8, 2, 16), 1, 1);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        double x = e.GetPosition(this).X, toStart = Math.Abs(x - X(_start)), toEnd = Math.Abs(x - X(_end));
        _grab = toStart < Reach && toStart <= toEnd ? Grab.Start : toEnd < Reach ? Grab.End : Grab.Playhead;
        CaptureMouse();
        Apply(x);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_grab != Grab.None) Apply(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _grab = Grab.None;
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) => _grab = Grab.None;

    private void Apply(double x)
    {
        var at = TimeAt(x);
        switch (_grab)
        {
            case Grab.Start: StartAsked?.Invoke(at); break;
            case Grab.End: EndAsked?.Invoke(at); break;
            case Grab.Playhead: SeekAsked?.Invoke(at); break;
        }
    }
}
