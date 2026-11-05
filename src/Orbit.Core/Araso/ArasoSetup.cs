using System.Diagnostics;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

namespace Orbit.Core.Araso;

/// <summary>How the elevated step ended; the process's exit code.</summary>
public enum ArasoSetupResult { Done = 0, NotElevated = 10, NoBundle = 11, CopyFailed = 12, RegisterFailed = 13, StateFailed = 14, AlreadyRunning = 15, CleanupFailed = 16 }

/// <summary>What the elevated step asks of Windows, apart so its order and its undoing can be tested.</summary>
public interface IArasoMachine
{
    /// <summary>Runs regsvr32 on a DLL. False if it reports a failure.</summary>
    bool Register(string dll, bool is32Bit);
    /// <summary>False if regsvr32 reports a failure.</summary>
    bool Unregister(string dll, bool is32Bit);
    void SetStartup(string command);
    void ClearStartup();
    void WriteState(string version, string folder);
    void ClearState();
    /// <summary>The empty keys Araso's unregistering leaves under HKLM (docs/plan/02-araso.md).</summary>
    void ClearLeftovers();
    /// <summary>A file in use goes when Windows restarts.</summary>
    void DeleteAtRestart(string path);
}

/// <summary>
/// The part that needs administrator rights: the bundled files into Program Files, where only
/// administrators can write and sandboxed apps can read, then registered with Windows. Every run
/// converges: it can follow a run that died anywhere, because what says "set up" is written last.
/// </summary>
public sealed class ArasoSetup(string bundle, string programRoot, IArasoMachine machine)
{
    private const string VersionFile = "version.txt", HostName = "ArasoHost.exe", TipName = "ArasoTip.dll";

    /// <summary>Elevated code doesn't follow a link out of the folder it was pointed at.</summary>
    private static readonly EnumerationOptions Inside = new()
    {
        RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false,
    };

    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Orbit", "Araso");

    /// <summary>The version in the bundle; null when this build has none.</summary>
    public static string? BundledVersion(string bundle)
    {
        var file = Path.Combine(bundle, VersionFile);
        return File.Exists(file) && File.Exists(Path.Combine(bundle, HostName)) && File.Exists(Path.Combine(bundle, TipName))
            ? File.ReadAllText(file).Trim()
            : null;
    }

    public ArasoSetupResult Install()
    {
        if (BundledVersion(bundle) is not { Length: > 0 } version) return ArasoSetupResult.NoBundle;
        string folder;
        try
        {
            folder = CopyVersion(version);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Araso's files could not be copied ({e.GetType().Name})");
            return ArasoSetupResult.CopyFailed;
        }

        string tip = Path.Combine(folder, TipName), tip32 = Path.Combine(folder, "x86", TipName);
        if (!machine.Register(tip, is32Bit: false) || (File.Exists(tip32) && !machine.Register(tip32, is32Bit: true)))
        {
            Undo(tip, tip32);
            return ArasoSetupResult.RegisterFailed;
        }
        try
        {
            machine.SetStartup($"\"{Path.Combine(folder, HostName)}\" --server");
            machine.WriteState(version, folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn($"Araso's state could not be written ({e.GetType().Name})");
            Undo(tip, tip32);
            return ArasoSetupResult.StateFailed;
        }
        return ArasoSetupResult.Done;
    }

    /// <summary>
    /// Takes it all back from Windows, going on past a step that fails. What says "set up" goes last,
    /// so a run that stops early can be run again. Files in use stay until a restart: the host is never stopped.
    /// </summary>
    public ArasoSetupResult Remove(string? installedFolder)
    {
        bool ok = true;
        if (installedFolder is not null) ok = Undo(Path.Combine(installedFolder, TipName), Path.Combine(installedFolder, "x86", TipName));
        else ok &= Attempt(machine.ClearStartup) & Attempt(machine.ClearState);
        ok &= Attempt(machine.ClearLeftovers);
        ok &= Attempt(() =>
        {
            if (Directory.Exists(programRoot)) DeleteWhatCan(programRoot);
        });
        return ok ? ArasoSetupResult.Done : ArasoSetupResult.CleanupFailed;
    }

    private bool Undo(string tip, string tip32)
    {
        bool ok = Attempt(() =>
        {
            if (!machine.Unregister(tip, is32Bit: false)) throw new IOException("regsvr32 failed");
        });
        if (File.Exists(tip32))
        {
            ok &= Attempt(() =>
            {
                if (!machine.Unregister(tip32, is32Bit: true)) throw new IOException("regsvr32 failed");
            });
        }
        return ok & Attempt(machine.ClearStartup) & Attempt(machine.ClearState);
    }

    private static bool Attempt(Action step)
    {
        try
        {
            step();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Warn($"A step of Araso's cleanup failed ({e.GetType().Name})");
            return false;
        }
    }

    /// <summary>A folder per version, never written into once complete, so DLLs in use are never replaced.</summary>
    private string CopyVersion(string version)
    {
        if (version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || version.StartsWith('.')) throw new IOException("Not a version");
        var folder = Path.Combine(programRoot, version);
        var files = Directory.GetFiles(bundle, "*", Inside).Select(f => Path.GetRelativePath(bundle, f)).ToList();
        if (Directory.Exists(folder) && IsComplete(folder, files)) return folder;

        var draft = folder + ".part";
        if (Directory.Exists(draft)) Directory.Delete(draft, recursive: true);
        foreach (var file in files)
        {
            var target = Path.Combine(draft, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(bundle, file), target);
        }
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.Move(draft, folder);
        return folder;
    }

    private bool IsComplete(string folder, List<string> files) => files.All(file =>
    {
        var copy = new FileInfo(Path.Combine(folder, file));
        return copy.Exists && copy.Length == new FileInfo(Path.Combine(bundle, file)).Length;
    });

    private void DeleteWhatCan(string root)
    {
        foreach (var file in Directory.GetFiles(root, "*", Inside))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                machine.DeleteAtRestart(file);
            }
        }
        foreach (var folder in Directory.GetDirectories(root, "*", Inside).OrderByDescending(f => f.Length).Append(root))
        {
            try
            {
                Directory.Delete(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                machine.DeleteAtRestart(folder);
            }
        }
    }
}

/// <summary>The real machine: regsvr32, the HKLM values, delayed deletes.</summary>
public sealed class WindowsArasoMachine : IArasoMachine
{
    private const string StateKey = @"SOFTWARE\Orbit\Araso", RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", RunValue = "OrbitAraso";
    private const string TipKey = @"SOFTWARE\Microsoft\CTF\TIP\{6D6C7D5F-3B7F-4F0E-9A2C-417261736F01}", TipKey32 = @"SOFTWARE\WOW6432Node\Microsoft\CTF\TIP\{6D6C7D5F-3B7F-4F0E-9A2C-417261736F01}";

    private static int RegSvr(string dll, bool is32Bit, bool remove)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var tool = Path.Combine(windows, is32Bit ? "SysWOW64" : "System32", "regsvr32.exe");
        using var process = Process.Start(new ProcessStartInfo(tool, $"/s {(remove ? "/u " : "")}\"{dll}\"") { UseShellExecute = false, CreateNoWindow = true })!;
        if (!process.WaitForExit(60_000))
        {
            process.Kill();
            return -1;
        }
        return process.ExitCode;
    }

    public bool Register(string dll, bool is32Bit)
    {
        int code = RegSvr(dll, is32Bit, remove: false);
        if (code != 0) Log.Warn($"regsvr32 failed with {code} ({(is32Bit ? "32" : "64")}-bit)");
        return code == 0;
    }

    public bool Unregister(string dll, bool is32Bit)
    {
        if (!File.Exists(dll)) return true;
        int code = RegSvr(dll, is32Bit, remove: true);
        if (code != 0) Log.Warn($"regsvr32 /u failed with {code} ({(is32Bit ? "32" : "64")}-bit)");
        return code == 0;
    }

    public void SetStartup(string command)
    {
        using var key = Registry.LocalMachine.CreateSubKey(RunKey, writable: true);
        key.SetValue(RunValue, command);
    }

    public void ClearStartup()
    {
        using var key = Registry.LocalMachine.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    public void WriteState(string version, string folder)
    {
        using var key = Registry.LocalMachine.CreateSubKey(StateKey, writable: true);
        key.SetValue("Folder", folder);
        key.SetValue("Version", version);
    }

    public void ClearState() => Registry.LocalMachine.DeleteSubKeyTree(StateKey, throwOnMissingSubKey: false);

    public void ClearLeftovers()
    {
        Registry.LocalMachine.DeleteSubKeyTree(TipKey, throwOnMissingSubKey: false);
        Registry.LocalMachine.DeleteSubKeyTree(TipKey32, throwOnMissingSubKey: false);
    }

    public void DeleteAtRestart(string path)
    {
        if (!PInvoke.MoveFileEx(path, null, MOVE_FILE_FLAGS.MOVEFILE_DELAY_UNTIL_REBOOT))
            Log.Warn("A file of Araso's could not be marked for deletion at restart");
    }

    /// <summary>Where setup put it, from a key only administrators can write; nulls when it isn't set up.</summary>
    public static (string? Version, string? Folder) ReadState()
    {
        using var key = Registry.LocalMachine.OpenSubKey(StateKey);
        return (key?.GetValue("Version") as string, key?.GetValue("Folder") as string);
    }
}

/// <summary>What Orbit.exe does when started elevated for Araso, before anything else of the app.</summary>
public static class ArasoElevated
{
    public const string Setup = "--araso-setup", Remove = "--araso-remove";

    /// <summary>The exit code, or null when the arguments aren't these. Only ever works from Orbit's own folder: no path is taken from outside.</summary>
    public static int? Run(string[] args)
    {
        if (args is not [Setup or Remove]) return null;
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            return (int)ArasoSetupResult.NotElevated;
        using var only = new Mutex(true, @"Global\Orbit.ArasoSetup", out bool first);
        if (!first) return (int)ArasoSetupResult.AlreadyRunning;
        var setup = new ArasoSetup(Path.Combine(AppContext.BaseDirectory, "araso"), ArasoSetup.DefaultRoot, new WindowsArasoMachine());
        try
        {
            return (int)(args[0] == Setup ? setup.Install() : setup.Remove(WindowsArasoMachine.ReadState().Folder));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Error("Araso's elevated step failed", e);
            return (int)ArasoSetupResult.CleanupFailed;
        }
    }
}
