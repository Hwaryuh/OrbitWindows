using System.Diagnostics;
using Orbit.Core;

namespace Orbit.App;

/// <summary>Opening things in Windows' own apps.</summary>
internal static class Shell
{
    public static void OpenLogFolder() => Process.Start(new ProcessStartInfo(Log.Folder) { UseShellExecute = true });
}
