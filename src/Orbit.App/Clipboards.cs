using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Orbit.Core.Clipboard;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;

namespace Orbit.App;

/// <summary>
/// Writing the clipboard, which another app may hold for a moment. Every write carries Orbit's
/// marker format, so the clipboard history and Translate know it was Orbit's own.
/// </summary>
internal static class Clipboards
{
    /// <summary>Text onto the clipboard, then a toast: <paramref name="message"/>, or that copying failed.</summary>
    public static bool CopyText(string text, string message, string? detail = null)
    {
        var data = new DataObject();
        data.SetText(text);
        bool copied = Set(data);
        Overlays.Toast.Show(copied ? message : Strings.CopyFailed, copied ? detail : null);
        return copied;
    }

    /// <summary>Puts <paramref name="data"/> on the clipboard, marked as Orbit's. False if another app held it the whole time.</summary>
    public static bool Set(DataObject data) => Set(data, out _);

    /// <param name="sequence">The clipboard sequence number read at once after the write: the write's own.</param>
    public static bool Set(DataObject data, out uint sequence)
    {
        sequence = 0;
        // A stream goes onto the clipboard as is; other objects would need a serializer.
        data.SetData(ClipboardRules.OwnFormat, new System.IO.MemoryStream([1]));
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                sequence = PInvoke.GetClipboardSequenceNumber();
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(30);
            }
        }
        return false;
    }

    /// <summary>Windows' count of clipboard changes: compared before and after, it tells whether anyone wrote meanwhile.</summary>
    public static uint Sequence() => PInvoke.GetClipboardSequenceNumber();

    private const uint CfUnicodeText = 13;
    private static HwndSource? _owner;

    /// <summary>
    /// Replaces the clipboard with <paramref name="text"/> (marked as Orbit's) only if it is still
    /// change <paramref name="expected"/>: the check and the write happen while Orbit holds the
    /// clipboard open, so a copy made in between is never overwritten. UI thread only.
    /// </summary>
    public static unsafe bool SetTextIfUnchanged(string text, uint expected)
    {
        // EmptyClipboard needs an owner window, or the write that follows fails.
        _owner ??= new HwndSource(new HwndSourceParameters("Orbit clipboard owner") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        var owner = new HWND(_owner.Handle);
        bool opened = false;
        for (int attempt = 0; attempt < 5 && !(opened = PInvoke.OpenClipboard(owner)); attempt++) Thread.Sleep(30);
        if (!opened) return false;
        try
        {
            if (PInvoke.GetClipboardSequenceNumber() != expected) return false;
            if (!PInvoke.EmptyClipboard()) return false;
            if (!Put(CfUnicodeText, MemoryMarshal.AsBytes((text + "\0").AsSpan()))) return false;
            uint own = PInvoke.RegisterClipboardFormat(ClipboardRules.OwnFormat);
            if (own != 0) Put(own, [1]);
            return true;
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    /// <summary>One format onto the open clipboard; Windows owns the memory once it is set.</summary>
    private static unsafe bool Put(uint format, ReadOnlySpan<byte> bytes)
    {
        var memory = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, (nuint)bytes.Length);
        if (memory.Value == null) return false;
        void* target = PInvoke.GlobalLock(memory);
        if (target == null)
        {
            PInvoke.GlobalFree(memory);
            return false;
        }
        bytes.CopyTo(new Span<byte>(target, bytes.Length));
        PInvoke.GlobalUnlock(memory);
        if (PInvoke.SetClipboardData(format, new HANDLE(memory.Value)).IsNull)
        {
            PInvoke.GlobalFree(memory);
            return false;
        }
        return true;
    }
}
