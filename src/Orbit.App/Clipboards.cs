using System.Windows;
using Orbit.Core.Clipboard;
using Windows.Win32;

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
}
