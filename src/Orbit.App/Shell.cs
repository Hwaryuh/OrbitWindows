using System.Diagnostics;
using Orbit.Core;

namespace Orbit.App;

/// <summary>Opening things in Windows' own apps.</summary>
internal static class Shell
{
    public static void OpenLogFolder() => Process.Start(new ProcessStartInfo(Log.Folder) { UseShellExecute = true });

    /// <summary>A File Explorer window with the file selected.</summary>
    public static void Reveal(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });

    /// <summary>The file in its default app.</summary>
    public static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
