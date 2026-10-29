using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orbit.App.Features.Capture;
using Orbit.App.Features.Clipboard;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Actions;
using Orbit.Core.Capture;
using Orbit.Core.Clipboard;
using Orbit.Core.Input;
using Orbit.Core.Settings;

namespace Orbit.App.Features;

/// <summary>
/// The clipboard history (Orbit: Modules/Clipboard): every copy that isn't private, Orbit's own,
/// from an ignored program or made while paused goes into the history; Win+Alt+V opens the panel
/// (docs/plan/04-shortcuts.md). Pictures get their text recognized for search, one at a time.
/// </summary>
internal sealed class ClipboardFeature : IFeature
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly SemaphoreSlim _ocr = new(1, 1);
    private AppServices? _services;
    private ClipHistory? _history;
    private ClipboardPanel? _panel;
    private DispatcherTimer? _saveTimer;
    private int _hotkey;

    public ClipHistory History => _history!;

    public void Start(AppServices services)
    {
        _services = services;
        var prefs = services.Preferences;
        _history = new ClipHistory(Path.Combine(AppPaths.RoamingFolder(), "clipboard.json"), AppPaths.LocalFolder("Clips"), () => prefs.Get<int>(Pref.HistoryLimit));
        int orphans = _history.RemoveOrphans();
        if (orphans > 0) Log.Info($"Removed {orphans} clipboard files no item used");
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveSoon();
        _history.Changed += () =>
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        };
        services.Clipboard.Copied += OnCopied;
        services.ClipboardHistory = this;
        _hotkey = services.AddHotkey(new Hotkey(Modifiers.Win | Modifiers.Alt, 0x56), OrbitAction.Clipboard);
        services.Actions.Register(OrbitAction.Clipboard, Toggle);
    }

    public void OnHook(HookEvent e)
    {
        if (e.Notice == HookNotice.HotkeyDown && e.Hotkey == _hotkey) Toggle();
    }

    public void Dispose()
    {
        _saveTimer?.Stop();
        if (_history is { } history) history.Write(history.Snapshot());
        _panel?.Close();
    }

    private void SaveSoon()
    {
        _saveTimer!.Stop();
        var snapshot = _history!.Snapshot();
        _ = Task.Run(() => _history.Write(snapshot));
    }

    // MARK: Recording

    public bool Paused => ClipboardRules.IsPaused(_services!.Preferences.Get<double>(Pref.PausedUntil), DateTimeOffset.Now);

    /// <summary>Pauses recording for a while, or until resumed (null).</summary>
    public void Pause(TimeSpan? span)
    {
        var until = span is { } s ? DateTimeOffset.Now.Add(s).ToUnixTimeSeconds() : double.MaxValue;
        _services!.Preferences.Set(Pref.PausedUntil, (double)until);
    }

    public void Resume() => _services!.Preferences.Set(Pref.PausedUntil, 0.0);

    private void OnCopied(ClipboardCopy copy)
    {
        if (copy.Own || copy.Private || Paused) return;
        var services = _services!;
        var process = copy.OwnerProcess ?? services.Foreground.ForegroundProcess;
        if (ClipboardRules.IsIgnored(process, services.Preferences.Get<string[]>(Pref.IgnoredApps))) return;
        var history = _history!;
        ClipItem? item;
        try
        {
            item = Build(copy, history);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("A copy could not be stored in the clipboard history", e);
            return;
        }
        if (item is null) return;
        item = history.Insert(item with { AppProcess = process, AppName = copy.OwnerName });
        if (item.Kind == ClipKind.Image)
        {
            if (!history.EnforceBudget()) Log.Warn("Pinned clipboard pictures alone are over the folder budget");
            if (item.OcrText is null && item.ImageFile is { } file) _ = RecognizeAsync(item.Id, history.PathOf(file));
        }
    }

    /// <summary>The history entry for a copy, its side files written; null when there is nothing to keep.</summary>
    private static ClipItem? Build(ClipboardCopy copy, ClipHistory history)
    {
        if (copy.Files is { Count: > 0 } files)
            return new ClipItem { Kind = ClipKind.File, FilePaths = files, Signature = ClipItem.SignatureOf(string.Join("|", files)) };
        if (copy.Png is { } png)
        {
            var signature = ClipItem.SignatureOf(png);
            // The same picture again keeps its stored file.
            if (history.Items.FirstOrDefault(i => i.Signature == signature) is { ImageFile: { } kept } existing && File.Exists(history.PathOf(kept))) return existing;
            var name = $"{Guid.NewGuid():N}.png";
            File.WriteAllBytes(history.PathOf(name), png);
            return new ClipItem { Kind = ClipKind.Image, ImageFile = name, ImageWidth = copy.ImageWidth, ImageHeight = copy.ImageHeight, Signature = signature };
        }
        if (copy.Text is not { } text) return null;
        string? rtf = null, html = null;
        if (copy.Rtf is { } rich)
        {
            rtf = $"{Guid.NewGuid():N}.rtf";
            File.WriteAllText(history.PathOf(rtf), rich);
        }
        if (copy.Html is { } markup)
        {
            html = $"{Guid.NewGuid():N}.html";
            File.WriteAllText(history.PathOf(html), markup);
        }
        return new ClipItem { Kind = ClipItem.Classify(text), Text = text, RtfFile = rtf, HtmlFile = html, Signature = ClipItem.SignatureOf(text) };
    }

    /// <summary>A picture's words, for search; one picture at a time, and none if the item went meanwhile.</summary>
    private async Task RecognizeAsync(Guid id, string path)
    {
        if (!TextRecognition.IsAvailable) return;
        await _ocr.WaitAsync();
        try
        {
            if (_history!.Find(id) is null || CaptureItem.Load(path) is not { } image) return;
            var text = await TextRecognition.RecognizeAsync(FrameImages.ToFrame(image.Image));
            if (!string.IsNullOrWhiteSpace(text)) _history.SetOcrText(id, text);
        }
        catch (Exception e)
        {
            Log.Warn($"Text recognition of a clipboard picture failed ({e.GetType().Name})");
        }
        finally
        {
            _ocr.Release();
        }
    }

    // MARK: Using an item

    private void Toggle()
    {
        _panel ??= new ClipboardPanel(this, _services!);
        if (_panel.IsVisible) _panel.Dismiss();
        else _panel.Present();
    }

    /// <summary>The item back on the clipboard (Orbit's own write); plain drops rich text, and puts a picture's words.</summary>
    public bool Copy(ClipItem item, bool plain) => Copy(item, plain, out _);

    /// <param name="sequence">The clipboard sequence number of this write.</param>
    public bool Copy(ClipItem item, bool plain, out uint sequence)
    {
        sequence = 0;
        var history = _history!;
        var data = new DataObject();
        switch (item.Kind)
        {
            case ClipKind.Image:
                if (plain && item.OcrText is { } words) data.SetText(words);
                else if (item.ImageFile is { } file && ReadBytes(history.PathOf(file)) is { } png && Decode(png) is { } image)
                {
                    data.SetImage(image);
                    data.SetData("PNG", new MemoryStream(png));
                }
                else return false;
                break;
            case ClipKind.File:
                var list = new StringCollection();
                list.AddRange((item.FilePaths ?? []).ToArray());
                data.SetFileDropList(list);
                break;
            default:
                data.SetText(item.Text ?? "");
                if (!plain)
                {
                    if (item.RtfFile is { } rtf && Read(history.PathOf(rtf)) is { } rich) data.SetData(DataFormats.Rtf, rich);
                    if (item.HtmlFile is { } html && Read(history.PathOf(html)) is { } markup) data.SetData(DataFormats.Html, markup);
                }
                break;
        }
        if (!Clipboards.Set(data, out sequence)) return false;
        history.MarkUsed(item.Id);
        return true;
    }

    private static byte[]? ReadBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static BitmapSource? Decode(byte[] png)
    {
        try
        {
            var image = BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or IOException)
        {
            return null;
        }
    }

    private static string? Read(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Copies the item, then pastes it into <paramref name="target"/> (or only copies, as clipboard.pasteDirectly says).</summary>
    public async Task PasteAsync(ClipItem item, bool plain, nint target)
    {
        if (!Copy(item, plain, out uint written))
        {
            Toast.Show(Strings.CopyFailed);
            return;
        }
        if (!_services!.Preferences.Get<bool>(Pref.PasteDirectly))
        {
            Toast.Show(Strings.Copied);
            return;
        }
        var result = await Paster.PasteAsync(target, () => Clipboards.Sequence() == written);
        if (result != PasteResult.Pasted) Log.Info($"Clipboard paste did not happen: {result}");
        switch (result)
        {
            case PasteResult.TargetGone:
                Toast.Show(Strings.ClipNothingToPaste);
                break;
            case PasteResult.TargetNotInFront:
                Toast.Show(Strings.ClipNotInFront);
                break;
            case PasteResult.InputBlocked:
                Toast.Show(Strings.ClipBlocked);
                break;
            case PasteResult.ClipboardChanged:
                Toast.Show(Strings.ClipChanged);
                break;
        }
    }

    /// <summary>The thumbnail of a picture item, decoded small; null when its file is gone.</summary>
    public BitmapSource? Thumbnail(ClipItem item, int width)
    {
        if (item.ImageFile is not { } file) return null;
        try
        {
            using var stream = new FileStream(_history!.PathOf(file), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = width;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }
}
