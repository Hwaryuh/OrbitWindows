using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orbit.Core;
using Orbit.Core.Capture;
using Orbit.Core.Capture.Editor;

namespace Orbit.App.Features.Capture.Editor;

/// <summary>
/// The picture blurred or pixelated at a strength, made once off the UI thread and reused (Orbit:
/// <c>EditorModel.redacted</c>). Only the last few are kept: an 8K picture is 127 MB per copy.
/// Until one is ready the canvas covers the area instead; <see cref="Ready"/> asks for a redraw.
/// </summary>
internal sealed class RedactionCache(ScreenFrame source, double dpi)
{
    private const int Keep = 3;

    private readonly LinkedList<((EditorTool Tool, int Level) Key, BitmapSource Image)> _made = [];
    private readonly HashSet<(EditorTool, int)> _making = [];
    // Never retried: the area stays covered rather than logging on every redraw.
    private readonly HashSet<(EditorTool, int)> _failed = [];
    private bool _closed;

    public event Action? Ready;

    /// <summary>The picture if it is made; otherwise null, and it is being made.</summary>
    public BitmapSource? Get(EditorTool tool, int level)
    {
        var key = (tool, Math.Clamp(level, 1, 10));
        if (Find(key) is { } made) return made;
        if (!_closed && !_failed.Contains(key) && _making.Add(key)) _ = MakeAsync(key);
        return null;
    }

    private BitmapSource? Find((EditorTool, int) key)
    {
        for (var node = _made.First; node is not null; node = node.Next)
        {
            if (node.Value.Key != key) continue;
            _made.Remove(node);
            _made.AddFirst(node);
            return node.Value.Image;
        }
        return null;
    }

    /// <summary>The picture now, made on this thread if need be: for export, which must never show a placeholder.</summary>
    /// <remarks>Null only if making it fails; the renderer then covers the area.</remarks>
    public BitmapSource? GetNow(EditorTool tool, int level)
    {
        var key = (tool, Math.Clamp(level, 1, 10));
        if (Find(key) is { } made) return made;
        try
        {
            var image = Make(key);
            Remember(key, image);
            return image;
        }
        catch (Exception e)
        {
            Log.Error($"Making the {key.Item1} picture failed", e);
            return null;
        }
    }

    /// <summary>The editor closed: results still on their way are dropped.</summary>
    public void Close()
    {
        _closed = true;
        _made.Clear();
    }

    private async Task MakeAsync((EditorTool Tool, int Level) key)
    {
        try
        {
            var image = await Task.Run(() => Make(key));
            if (_closed) return;
            Remember(key, image);
            Ready?.Invoke();
        }
        catch (Exception e)
        {
            _failed.Add(key);
            Log.Error($"Making the {key.Tool} picture failed", e);
        }
        finally
        {
            _making.Remove(key);
        }
    }

    private BitmapSource Make((EditorTool Tool, int Level) key)
    {
        var pixels = key.Tool == EditorTool.Blur
            ? Redaction.Blur(source.Pixels, source.Width, source.Height, source.Stride, Redaction.BlurRadius(key.Level, source.Width))
            : Redaction.Pixelate(source.Pixels, source.Width, source.Height, source.Stride, Redaction.PixelCell(key.Level, source.Width));
        var image = BitmapSource.Create(source.Width, source.Height, dpi, dpi, PixelFormats.Bgr32, null, pixels, source.Stride);
        image.Freeze();
        return image;
    }

    private void Remember((EditorTool, int) key, BitmapSource image)
    {
        if (_closed || _made.Any(m => m.Key == key)) return;
        _made.AddFirst((key, image));
        while (_made.Count > Keep) _made.RemoveLast();
    }
}
