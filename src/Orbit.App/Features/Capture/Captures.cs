using System.IO;
using System.Windows.Media.Imaging;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Capture;

namespace Orbit.App.Features.Capture;

/// <summary>The shutter sound (capture.sound), made once and played without waiting.</summary>
internal static class Shutter
{
    private static System.Media.SoundPlayer? _player;

    public static void Play()
    {
        try
        {
            _player ??= new System.Media.SoundPlayer(new MemoryStream(ShutterSound.Wave()));
            _player.Play();
        }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            Log.Warn($"The shutter sound could not be played ({e.GetType().Name})");
        }
    }
}

/// <summary>
/// A capture in the library: its file and its picture (Orbit: <c>Capture</c>). For a recording the
/// picture is a frame of it and <see cref="Length"/> may be known. Compared by identity.
/// </summary>
internal sealed class CaptureItem(string path, BitmapSource image, TimeSpan? length = null)
{
    public string Path { get; } = path;
    public BitmapSource Image { get; } = image;
    public TimeSpan? Length { get; } = length;

    /// <summary>A recording: it plays in the video player, has its own editor, and no pin or text.</summary>
    public bool IsVideo => CaptureLibrary.IsVideo(Path);

    /// <summary>The picture read from a library file (a recording's thumbnail); null when it can't be read.</summary>
    public static CaptureItem? Load(string path)
    {
        if (CaptureLibrary.IsVideo(path))
        {
            if (File.Exists(path) && VideoThumbnails.Read(path) is { } frame) return new CaptureItem(path, frame);
            Log.Warn("A recording's thumbnail could not be read");
            return null;
        }
        try
        {
            using var stream = File.OpenRead(path);
            var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            image.Freeze();
            return new CaptureItem(path, image);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            Log.Warn($"A capture file could not be read ({e.GetType().Name})");
            return null;
        }
    }
}

/// <summary>
/// What can be done with a capture from anywhere (the cards, a pin, the selection's keys): copy it,
/// save a copy, read its text, show it in File Explorer, throw it away. Each says how it went in a
/// toast (Orbit: CaptureService.swift and QuickAccess.swift's actions).
/// </summary>
internal sealed class CaptureActions(CaptureStore store)
{
    public CaptureStore Store { get; } = store;

    public bool Copy(CaptureItem item)
    {
        if (item.IsVideo) return CopyVideo(item.Path);
        byte[] png;
        try
        {
            png = File.Exists(item.Path) ? File.ReadAllBytes(item.Path) : FrameImages.Png(item.Image);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            png = FrameImages.Png(item.Image);
        }
        bool copied = FrameImages.Copy(item.Image, png);
        Toast.Show(copied ? Strings.CaptureCopied : Strings.CopyFailed);
        return copied;
    }

    /// <summary>
    /// A recording goes onto the clipboard as its file, which pastes into File Explorer and chats, and
    /// as its quoted path, which is what a terminal or a text box takes (they read only text).
    /// </summary>
    public static bool CopyVideo(string path)
    {
        if (!File.Exists(path))
        {
            Toast.Show(Strings.CaptureFileMissing);
            return false;
        }
        var data = new System.Windows.DataObject();
        data.SetData(System.Windows.DataFormats.FileDrop, new[] { path });
        data.SetData(System.Windows.DataFormats.UnicodeText, $"\"{path}\"");
        bool copied = Clipboards.Set(data);
        Toast.Show(copied ? Strings.VideoCopied : Strings.CopyFailed);
        return copied;
    }

    /// <summary>A copy in the save folder.</summary>
    public bool Save(CaptureItem item)
    {
        if (!File.Exists(item.Path))
        {
            Toast.Show(Strings.CaptureFileMissing);
            return false;
        }
        try
        {
            var saved = Store.SaveCopy(item.Path);
            Toast.Show(Strings.CaptureSaved, System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(saved)));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("Capture could not be saved to the save folder", e);
            Toast.Show(Strings.CaptureSaveFailed);
            return false;
        }
    }

    public void Reveal(CaptureItem item)
    {
        if (File.Exists(item.Path)) Shell.Reveal(item.Path);
        else Toast.Show(Strings.CaptureFileMissing);
    }

    /// <summary>Into the Recycle Bin and off the recent list. False when it is still there (cancelled or failed).</summary>
    public bool Trash(CaptureItem item) => Trash(item.Path);

    public bool Trash(string path)
    {
        switch (RecycleBin.Send(path))
        {
            case RecycleBin.Outcome.Moved or RecycleBin.Outcome.Missing:
                Store.Forget(path);
                Toast.Show(Strings.MovedToRecycleBin, Strings.MovedToRecycleBinDetail);
                return true;
            case RecycleBin.Outcome.Cancelled:
                return false;
            default:
                Toast.Show(Strings.RecycleFailed);
                return false;
        }
    }

    /// <summary>
    /// The text in the picture onto the clipboard (Orbit: <c>recognizeText</c>); a QR code or
    /// barcode in it wins over the words, and its payload is copied instead.
    /// </summary>
    public static async Task RecognizeAsync(ScreenFrame frame)
    {
        var codes = await Task.Run(() => Barcodes.Read(frame));
        if (codes.Count > 0)
        {
            var code = codes[0];
            var flat = code.ReplaceLineEndings(" ");
            Clipboards.CopyText(code, Strings.CodeCopied, flat.Length > 80 ? flat[..80] + "…" : flat);
            return;
        }
        var text = await TextRecognition.RecognizeAsync(frame);
        if (text is null)
        {
            Toast.Show(Strings.TextNoLanguage);
            return;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            Toast.Show(Strings.TextNotFound);
            return;
        }
        int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var line = text.ReplaceLineEndings(" ");
        Clipboards.CopyText(text, Strings.TextCopied(words), line.Length > 80 ? line[..80] + "…" : line);
    }

    public static Task RecognizeAsync(BitmapSource image) => RecognizeAsync(FrameImages.ToFrame(image));
}
