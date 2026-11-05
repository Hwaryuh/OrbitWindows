using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Araso;

namespace Orbit.App.Features;

/// <summary>
/// Araso, the Korean/English input method, whose code lives in src/Araso. Nothing happens on its
/// own: every switch starts from a click. The host is never stopped: an app still holding the text
/// service types garbage without it.
/// </summary>
internal sealed class ArasoFeature : IFeature
{
    public ArasoFeature() : this(ArasoFileStore.DefaultFolder) { }

    /// <param name="folder">Where Araso's files are.</param>
    public ArasoFeature(string folder) => Files = new ArasoFileStore(folder, ArasoSystem.SettingsWindowOpen, ArasoSystem.TellHostToReload);

    public ArasoFileStore Files { get; }

    /// <summary>Whether this build carries Araso.</summary>
    public bool Bundled { get; } = ArasoSetup.BundledVersion(Path.Combine(AppContext.BaseDirectory, "araso")) is not null;

    public ArasoState State { get; private set; } = ArasoState.Unknown;

    /// <summary>Has <see cref="RefreshAsync"/> finished once.</summary>
    public bool Known { get; private set; }

    /// <summary>What is going on, for the page; null when nothing is.</summary>
    public string? Working { get; private set; }

    public event Action? Changed;

    public void Start(AppServices services) => services.Araso = this;

    /// <summary>Reads the state again and always tells the page, so a failed read can't leave it waiting.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            State = await ArasoSystem.DetectAsync();
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidCastException or Win32Exception or IOException or ArgumentException)
        {
            Log.Warn($"Araso's state could not be read ({e.GetType().Name})");
            State = ArasoState.Unknown;
        }
        Known = true;
        Changed?.Invoke();
    }

    private void Say(string? working)
    {
        Working = working;
        Changed?.Invoke();
    }

    /// <summary>Makes Araso the input method of this session. Without its host it isn't switched to at all.</summary>
    public async Task EnableAsync()
    {
        if (Working is not null) return;
        Say(Strings.ArasoSwitching);
        try
        {
            if (!await SetUpAsync()) return;
            if (!await EnsureHostAsync()) Toast.Show(Strings.ArasoHostFailed);
            else if (await ArasoSystem.UseArasoAsync()) Toast.Show(Strings.ArasoOn);
            else Toast.Show(Strings.ArasoSwitchFailed);
        }
        finally
        {
            Working = null;
            await RefreshAsync();
        }
    }

    /// <summary>The first time: the one step that takes administrator rights, then this user's list. False if it can't go on.</summary>
    private async Task<bool> SetUpAsync()
    {
        await RefreshAsync();
        if (!State.IsInstalled || !State.Registered)
        {
            Say(Strings.ArasoSettingUp);
            if (await RunElevatedAsync(ArasoElevated.Setup) is not { } code) return false;
            if (code != (int)ArasoSetupResult.Done)
            {
                Log.Warn($"Araso's setup ended with {(ArasoSetupResult)code}");
                Toast.Show(Strings.ArasoSetupFailed);
                return false;
            }
            await RefreshAsync();
        }
        if (State.InstallFolder is not { } folder) return false;
        if (!State.Enabled && !await ArasoSystem.SetOnUserListAsync(folder, on: true))
        {
            Log.Warn("Araso could not be put on the user's input list");
            Toast.Show(Strings.ArasoSetupFailed);
            return false;
        }
        // Orbit's own build has no release of its own to look for.
        if (Files.Edit(ArasoFileStore.Settings, ArasoFiles.WithUpdatesOff) != ArasoEdit.Done) Log.Warn("Araso's own update check could not be turned off");
        Say(Strings.ArasoSwitching);
        return true;
    }

    /// <summary>The exit code of Orbit.exe run elevated; null when the user said no to the prompt, already said.</summary>
    private static async Task<int?> RunElevatedAsync(string argument)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, argument) { UseShellExecute = true, Verb = "runas" })
                ?? throw new IOException("It did not start");
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            Toast.Show(Strings.ArasoSetupCancelled);
            return null;
        }
        catch (Exception e) when (e is Win32Exception or IOException)
        {
            Log.Warn($"The elevated step could not start ({e.GetType().Name})");
            Toast.Show(Strings.ArasoSetupFailed);
            return null;
        }
    }

    /// <summary>
    /// Takes Araso out of Windows. The Microsoft IME first, then this user's list, then the part that
    /// takes administrator rights. The host keeps running until a restart or sign-out, for apps holding it.
    /// </summary>
    public async Task RemoveAsync()
    {
        if (Working is not null) return;
        Say(Strings.ArasoSwitching);
        try
        {
            await RefreshAsync();
            if (State.Active && !await ArasoSystem.UseMicrosoftAsync())
            {
                Toast.Show(Strings.ArasoSwitchFailed);
                return;
            }
            var folder = State.InstallFolder;
            if (folder is not null && !await ArasoSystem.SetOnUserListAsync(folder, on: false))
            {
                Log.Warn("Araso could not be taken off the user's input list");
                Toast.Show(Strings.ArasoSetupFailed);
                return;
            }
            int? code = await RunElevatedAsync(ArasoElevated.Remove);
            if (code == (int)ArasoSetupResult.Done)
            {
                Toast.Show(Strings.ArasoRemoved, Strings.ArasoRemovedDetail);
                return;
            }
            // Nothing or not everything was removed, so it goes back on the list it just left.
            if (folder is not null) await ArasoSystem.SetOnUserListAsync(folder, on: true);
            if (code is not null) Toast.Show(Strings.ArasoSetupFailed);
        }
        finally
        {
            Working = null;
            await RefreshAsync();
        }
    }

    /// <summary>Starts a host that isn't running, for Araso in use or about to be.</summary>
    public async Task StartHostAsync()
    {
        if (Working is not null) return;
        Say(Strings.ArasoSwitching);
        try
        {
            if (!await EnsureHostAsync()) Toast.Show(Strings.ArasoHostFailed);
        }
        finally
        {
            Working = null;
            await RefreshAsync();
        }
    }

    private async Task<bool> EnsureHostAsync()
    {
        await RefreshAsync();
        if (State.HostRunning) return true;
        if (State.InstallFolder is not { } folder) return false;
        try
        {
            return await ArasoSystem.StartHostAsync(folder);
        }
        catch (Exception e) when (e is Win32Exception or IOException)
        {
            Log.Warn($"Araso's host could not be started ({e.GetType().Name})");
            return false;
        }
    }

    /// <summary>Back to the Microsoft IME, for this session. The host keeps running for apps that still hold the text service.</summary>
    public async Task<bool> DisableAsync()
    {
        if (Working is not null) return false;
        Say(Strings.ArasoSwitching);
        try
        {
            bool done = await ArasoSystem.UseMicrosoftAsync();
            if (done) Toast.Show(Strings.ArasoOff, Strings.ArasoOffDetail);
            else Toast.Show(Strings.ArasoSwitchFailed);
            return done;
        }
        finally
        {
            Working = null;
            await RefreshAsync();
        }
    }
}
