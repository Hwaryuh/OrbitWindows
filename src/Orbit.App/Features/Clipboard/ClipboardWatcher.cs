using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Orbit.App.Features.Capture;
using Orbit.Core;
using Orbit.Core.Clipboard;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Orbit.App.Features.Clipboard;

/// <summary>
/// One change of the clipboard as Orbit read it. <see cref="Own"/> and <see cref="Private"/>
/// copies carry no content. Never logged.
/// </summary>
internal sealed record ClipboardCopy(uint Sequence, DateTime At, bool Own, bool Private)
{
    /// <summary>The copying program's file name, lowercase, when Windows names an owner.</summary>
    public string? OwnerProcess { get; init; }
    public string? OwnerName { get; init; }
    public string? Text { get; init; }
    public string? Rtf { get; init; }
    public string? Html { get; init; }
    public byte[]? Png { get; init; }
    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }
    public IReadOnlyList<string>? Files { get; init; }
}

/// <summary>
/// Listens for clipboard changes (AddClipboardFormatListener) on a thread of its own, and reads
/// each change there: an app that renders its clipboard late or holds it locked then stalls only
/// this thread, never the UI. A change seen while reading is read again. <see cref="Copied"/> is
/// raised on the UI thread for every change, Orbit's own included (marked).
/// </summary>
internal sealed class ClipboardWatcher : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private const string PngFormat = "PNG";
    private const string HtmlFormat = "HTML Format";

    private Dispatcher? _ui;
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private Dispatcher? _worker;
    private HwndSource? _window;
    private bool _pending;
    private volatile bool _stopped;

    public event Action<ClipboardCopy>? Copied;

    /// <summary>Starts listening; <see cref="Copied"/> comes on the thread that calls this (the UI thread).</summary>
    public void Start()
    {
        if (_thread is not null) return;
        _ui = Dispatcher.CurrentDispatcher;
        _thread = new Thread(Run) { Name = "Orbit clipboard", IsBackground = true };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    /// <summary>Stops listening. Never waits long: a worker stuck in a late-rendering app is left behind (a background thread).</summary>
    public void Dispose()
    {
        if (_worker is not { } worker) return;
        _worker = null;
        _stopped = true;
        worker.BeginInvoke(() =>
        {
            if (_window is not null) PInvoke.RemoveClipboardFormatListener(new HWND(_window.Handle));
            _window?.Dispose();
        });
        worker.BeginInvokeShutdown(DispatcherPriority.Normal);
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        _worker = Dispatcher.CurrentDispatcher;
        // A message-only window: it never shows and takes no part in focus.
        _window = new HwndSource(new HwndSourceParameters("Orbit clipboard") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        _window.AddHook(Hook);
        if (!PInvoke.AddClipboardFormatListener(new HWND(_window.Handle))) Log.Error($"The clipboard listener could not start (error {Marshal.GetLastWin32Error()})");
        _ready.Set();
        Dispatcher.Run();
    }

    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != WmClipboardUpdate) return 0;
        handled = true;
        // A burst of updates is read once.
        if (!_pending)
        {
            _pending = true;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, ReadChange);
        }
        return 0;
    }

    private void ReadChange()
    {
        _pending = false;
        uint before = PInvoke.GetClipboardSequenceNumber();
        ClipboardCopy? copy;
        try
        {
            copy = ReadWithRetries(before);
        }
        catch (Exception e)
        {
            Log.Error("The clipboard could not be read", e);
            return;
        }
        if (PInvoke.GetClipboardSequenceNumber() != before)
        {
            // It changed while being read: what was read may mix two copies. The next update reads it again.
            return;
        }
        if (copy is not null) _ui!.BeginInvoke(() =>
        {
            if (!_stopped) Copied?.Invoke(copy);
        });
    }

    private static ClipboardCopy? ReadWithRetries(uint sequence)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return Read(sequence);
            }
            catch (COMException) when (attempt < 4)
            {
                // Another app holds the clipboard for a moment.
                Thread.Sleep(40);
            }
            catch (COMException)
            {
                Log.Warn("The clipboard stayed locked; this change was skipped");
                return null;
            }
        }
    }

    private static ClipboardCopy? Read(uint sequence)
    {
        var data = System.Windows.Clipboard.GetDataObject();
        if (data is null) return null;
        var formats = data.GetFormats(false).ToHashSet(StringComparer.Ordinal);
        var now = DateTime.Now;
        if (ClipboardRules.IsOwn(formats)) return new ClipboardCopy(sequence, now, Own: true, Private: false);
        int? historyFlag = formats.Contains(ClipboardRules.HistoryFormat) && data.GetData(ClipboardRules.HistoryFormat) is MemoryStream flag && flag.Length >= 4
            ? BitConverter.ToInt32(flag.ToArray(), 0) : null;
        if (ClipboardRules.IsPrivate(formats, historyFlag)) return new ClipboardCopy(sequence, now, Own: false, Private: true);

        var (process, name) = Owner();
        var copy = new ClipboardCopy(sequence, now, false, false) { OwnerProcess = process, OwnerName = name };

        if (formats.Contains(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            return copy with { Files = files };

        bool hasText = formats.Contains(DataFormats.UnicodeText) || formats.Contains(DataFormats.Text);
        if (!hasText && ReadImage(data, formats) is { } image)
            return copy with { Png = image.Png, ImageWidth = image.Width, ImageHeight = image.Height };

        if (!hasText || data.GetData(DataFormats.UnicodeText) is not string text || string.IsNullOrWhiteSpace(text)) return copy;
        return copy with
        {
            Text = text,
            Rtf = formats.Contains(DataFormats.Rtf) ? Limited(data.GetData(DataFormats.Rtf) as string) : null,
            Html = formats.Contains(HtmlFormat) ? Limited(data.GetData(HtmlFormat) as string) : null,
        };
    }

    private static string? Limited(string? rich) => rich is { Length: > 0 and <= ClipboardRules.RichTextLimit } ? rich : null;

    /// <summary>The picture as PNG, its size checked before it is decoded; null when there is none or it is too large.</summary>
    private static (byte[] Png, int Width, int Height)? ReadImage(System.Windows.IDataObject data, HashSet<string> formats)
    {
        if (formats.Contains(PngFormat) && data.GetData(PngFormat) is MemoryStream png)
        {
            var bytes = png.ToArray();
            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            if (!ClipboardRules.ImageFits(frame.PixelWidth, frame.PixelHeight)) return null;
            return (bytes, frame.PixelWidth, frame.PixelHeight);
        }
        if (formats.Contains(DataFormats.Dib) && data.GetData(DataFormats.Dib) is MemoryStream dib && dib.Length >= 12)
        {
            // BITMAPINFOHEADER: width at 4, height (negative when top-down) at 8.
            var head = new byte[12];
            dib.Position = 0;
            dib.ReadExactly(head);
            long width = BitConverter.ToInt32(head, 4), height = Math.Abs((long)BitConverter.ToInt32(head, 8));
            if (!ClipboardRules.ImageFits(width, height)) return null;
            if (System.Windows.Clipboard.GetImage() is not { } bitmap) return null;
            return (FrameImages.Png(bitmap), bitmap.PixelWidth, bitmap.PixelHeight);
        }
        return null;
    }

    /// <summary>The program that put this on the clipboard, as Windows names it; nulls when it doesn't.</summary>
    private static unsafe (string? Process, string? Name) Owner()
    {
        var owner = PInvoke.GetClipboardOwner();
        if (owner.IsNull) return (null, null);
        uint process = 0;
        PInvoke.GetWindowThreadProcessId(owner, &process);
        if (process == 0 || Processes.ImagePath(process) is not { } path) return (null, null);
        return (Path.GetFileName(path).ToLowerInvariant(), Processes.DisplayName(path));
    }
}
