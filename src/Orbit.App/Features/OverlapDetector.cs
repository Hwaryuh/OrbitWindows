using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Orbit.Core;
using Orbit.Core.Overlaps;

namespace Orbit.App.Features;

/// <summary>
/// Looks at this PC for <see cref="OverlapScan"/>, read-only and off the UI thread. Each source
/// stands alone: a locked file, a huge file or a key that won't open costs only that source.
/// </summary>
internal static class OverlapDetector
{
    private const long LargestFile = 1024 * 1024;
    private const string Uninstall = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public static Task<OverlapInput> DetectAsync() => Task.Run(() =>
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var run = ReadSmall(Path.Combine(local, "Microsoft", "PowerToys", "PowerToys Run", "settings.json"));
        var araso = ReadSmall(Path.Combine(roaming, "Araso", "apps.json"));
        return new OverlapInput(RunningPrograms(), InstalledPrograms(),
            run is null ? null : OverlapScan.ParsePowerToysRun(run),
            araso is not null && OverlapScan.ArasoTakesAltSpace(araso));
    });

    private static List<string> RunningPrograms()
    {
        var names = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    names.Add(process.ProcessName);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // It exited meanwhile.
                }
            }
        }
        return names;
    }

    private static List<string> InstalledPrograms()
    {
        var names = new List<string>();
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var list = hive.OpenSubKey(Uninstall);
                if (list is null) continue;
                foreach (var id in list.GetSubKeyNames())
                {
                    try
                    {
                        using var entry = list.OpenSubKey(id);
                        if (entry?.GetValue("DisplayName") is string name) names.Add(name);
                    }
                    catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
                    {
                        // One entry we may not read.
                    }
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
                Log.Info($"The uninstall list could not be read ({e.GetType().Name})");
            }
        }
        return names;
    }

    /// <summary>The file's text, without waiting on a lock or reading a file too big to be settings; else null.</summary>
    private static string? ReadSmall(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > LargestFile) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
