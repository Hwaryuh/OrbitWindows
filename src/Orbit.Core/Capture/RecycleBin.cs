using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace Orbit.Core.Capture;

/// <summary>Files into the Recycle Bin, where the user can still put them back (Orbit: <c>trashItem</c>).</summary>
public static class RecycleBin
{
    public enum Outcome { Moved, Missing, Cancelled, Failed }

    private const uint FoDelete = 3;
    private const ushort FofSilent = 0x4, FofNoConfirmation = 0x10, FofAllowUndo = 0x40, FofNoErrorUi = 0x400, FofWantNukeWarning = 0x4000;

    /// <summary>
    /// Moves one file without asking and without Windows' own error boxes. Where the drive has no
    /// Recycle Bin, Windows asks before deleting it for good; saying no is <see cref="Outcome.Cancelled"/>.
    /// </summary>
    public static unsafe Outcome Send(string path)
    {
        if (!File.Exists(path)) return Outcome.Missing;
        // pFrom is a list of names, each ended by a null, the list by another.
        fixed (char* from = path + "\0\0")
        {
            var op = new SHFILEOPSTRUCTW
            {
                wFunc = FoDelete,
                pFrom = from,
                fFlags = (ushort)(FofSilent | FofNoConfirmation | FofAllowUndo | FofNoErrorUi | FofWantNukeWarning),
            };
            int result = PInvoke.SHFileOperation(ref op);
            if (op.fAnyOperationsAborted) return Outcome.Cancelled;
            if (result != 0) Log.Warn($"Moving a file to the Recycle Bin failed with 0x{result:X}");
            // The file's absence is the proof; the result code alone is not reliable for one file.
            return File.Exists(path) ? Outcome.Failed : Outcome.Moved;
        }
    }
}
