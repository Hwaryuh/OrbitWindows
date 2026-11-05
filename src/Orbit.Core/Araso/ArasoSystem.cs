using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.DataExchange;
using Windows.Win32.UI.TextServices;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Orbit.Core.Araso;

/// <summary>
/// What Windows says about Araso, and switching input methods for this session. Reads never change
/// anything. Text services calls run on an STA thread of their own, away from the UI's own text
/// service, and a switch is checked afterwards, not assumed.
/// </summary>
public static class ArasoSystem
{
    private const string HostWindowClass = "ArasoHostWindow", SettingsWindowClass = "ArasoSettingsWindow";
    private const ushort Korean = 0x0412;
    private const nuint CopyReload = 1;

    private static readonly Guid ArasoTip = new("6D6C7D5F-3B7F-4F0E-9A2C-417261736F01"), ArasoProfile = new("6D6C7D5F-3B7F-4F0E-9A2C-417261736F02");
    private static readonly Guid MicrosoftTip = new("A028AE76-01B1-46C2-99C4-ACD9858AE02F"), MicrosoftProfile = new("B5FE1F02-D5F2-4445-9C03-C568F23C99A1");

    public static Task<ArasoState> DetectAsync() => OnSta(Detect);

    private static ArasoState Detect()
    {
        var (version, folder) = ReadInstall();
        var (registered, enabled, active) = ReadProfile();
        return new ArasoState(version, folder, registered, enabled, active, folder is not null && HostRunning(folder), RestartPending());
    }

    private static (string? Version, string? Folder) ReadInstall()
    {
        try
        {
            // HKLM only: a user can't point Orbit at a program of their choosing to start.
            var (version, folder) = WindowsArasoMachine.ReadState();
            if (version is null) return (null, null);
            return (version, folder is not null && File.Exists(Path.Combine(folder, "ArasoHost.exe")) ? folder : null);
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Araso's install couldn't be read ({e.GetType().Name})");
            return (null, null);
        }
    }

    private static unsafe (bool Registered, bool Enabled, bool Active) ReadProfile()
    {
        try
        {
            return WithManager(manager =>
            {
                Guid tip = ArasoTip, profile = ArasoProfile;
                TF_INPUTPROCESSORPROFILE found = default;
                manager.GetProfile(PInvoke.TF_PROFILETYPE_INPUTPROCESSOR, Korean, &tip, &profile, default, &found);
                // Not found can come back as success with nothing filled in.
                if (found.clsid != ArasoTip) return (false, false, false);
                return (true, (found.dwFlags & PInvoke.TF_IPP_FLAG_ENABLED) != 0, (found.dwFlags & PInvoke.TF_IPP_FLAG_ACTIVE) != 0);
            });
        }
        catch (COMException)
        {
            return (false, false, false);
        }
    }

    private static T WithManager<T>(Func<ITfInputProcessorProfileMgr, T> use)
    {
        var created = Activator.CreateInstance(Type.GetTypeFromCLSID(PInvoke.CLSID_TF_InputProcessorProfiles)!)!;
        try
        {
            return use((ITfInputProcessorProfileMgr)created);
        }
        finally
        {
            Marshal.ReleaseComObject(created);
        }
    }

    /// <summary>A host of this session from the install folder, with its pipe up. Waiting on a pipe uses no connection.</summary>
    private static bool HostRunning(string folder)
    {
        PInvoke.ProcessIdToSessionId((uint)Environment.ProcessId, out uint session);
        var expected = Path.GetFullPath(Path.Combine(folder, "ArasoHost.exe"));
        bool found = false;
        foreach (var process in Process.GetProcessesByName("ArasoHost"))
        {
            using (process)
            {
                try
                {
                    found |= process.SessionId == session
                        && string.Equals(process.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException)
                {
                    // It exited, or it isn't ours to look at.
                }
            }
        }
        return found && PInvoke.WaitNamedPipe($@"\\.\pipe\Araso.{session}.{Environment.UserName}", 1);
    }

    private static bool RestartPending()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            return key?.GetValue("PendingFileRenameOperations") is string[] pending
                && pending.Any(entry => entry.Contains("ArasoTip", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Puts Araso on this user's list of input methods, or takes it off, as the signed-in user.</summary>
    public static async Task<bool> SetOnUserListAsync(string folder, bool on)
    {
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(folder, "ArasoHost.exe"), on ? "--register" : "--unregister") { UseShellExecute = false, WorkingDirectory = folder });
        if (process is null) return false;
        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }

    /// <summary>Starts the host as this user and waits for its pipe. False if it doesn't come up.</summary>
    public static async Task<bool> StartHostAsync(string folder)
    {
        using var started = Process.Start(new ProcessStartInfo(Path.Combine(folder, "ArasoHost.exe"), "--server") { UseShellExecute = false, WorkingDirectory = folder });
        for (int wait = 0; wait < 50; wait++)
        {
            await Task.Delay(100);
            if (HostRunning(folder)) return true;
        }
        return false;
    }

    /// <summary>Makes Araso the session's input method and checks that it is. On any failure, back to the Microsoft IME.</summary>
    public static Task<bool> UseArasoAsync() => OnSta(() =>
    {
        if (Activate(ArasoTip, ArasoProfile) && ReadProfile().Active) return true;
        Log.Warn("Araso could not be made active; going back to the Microsoft IME");
        Activate(MicrosoftTip, MicrosoftProfile);
        return false;
    });

    /// <summary>The Microsoft IME for this session. True once Araso is no longer the one in use.</summary>
    public static Task<bool> UseMicrosoftAsync() => OnSta(() => Activate(MicrosoftTip, MicrosoftProfile) && !ReadProfile().Active);

    private static unsafe bool Activate(Guid tip, Guid profile)
    {
        try
        {
            return WithManager(manager =>
            {
                Guid clsid = tip, guid = profile;
                manager.ActivateProfile(PInvoke.TF_PROFILETYPE_INPUTPROCESSOR, Korean, &clsid, &guid, default, PInvoke.TF_IPPMF_FORSESSION | PInvoke.TF_IPPMF_ENABLEPROFILE);
                return true;
            });
        }
        catch (COMException e)
        {
            Log.Warn($"An input method could not be switched to (0x{e.HResult:X8})");
            return false;
        }
    }

    /// <summary>Araso's own settings window has unsaved changes of its own while it is open.</summary>
    public static bool SettingsWindowOpen() => !PInvoke.FindWindow(SettingsWindowClass, null).IsNull;

    /// <summary>Tells the host the files changed. It carries nothing: a window pretending to be the host learns nothing.</summary>
    public static unsafe void TellHostToReload()
    {
        var host = PInvoke.FindWindow(HostWindowClass, null);
        if (host.IsNull) return;
        byte empty = 0;
        var copy = new COPYDATASTRUCT { dwData = CopyReload, cbData = 0, lpData = &empty };
        nuint result;
        PInvoke.SendMessageTimeout(host, PInvoke.WM_COPYDATA, default, (nint)(&copy), SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG, 1000, &result);
    }

    private static Task<T> OnSta<T>(Func<T> work) => OwnThread.RunAsync(ApartmentState.STA, "Orbit Araso", work);
}
