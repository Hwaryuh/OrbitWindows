using System.Windows;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Actions;
using Orbit.Core.SystemInfo;

namespace Orbit.App.Features;

/// <summary>
/// The System actions (Orbit: Modules/System/SystemActions.swift) for the ring, the launcher and
/// the System page: lock, display off, dark mode, desktop icons, hidden files, empty the Recycle
/// Bin (asked first), eject every removable drive.
/// </summary>
internal sealed class SystemFeature : IFeature
{
    public void Start(AppServices services)
    {
        var actions = services.Actions;
        actions.Register(OrbitAction.LockScreen, () =>
        {
            if (!SystemActions.Lock()) Toast.Show(Strings.SysLockFailed);
        });
        actions.Register(OrbitAction.SleepDisplay, () => Later.Run(TimeSpan.FromMilliseconds(300), SystemActions.DisplayOff));
        actions.Register(OrbitAction.DarkMode, () => Toast.Show(SystemActions.ToggleDarkMode() ? Strings.SysDarkOn : Strings.SysDarkOff));
        actions.Register(OrbitAction.DesktopIcons, () => Toast.Show(SystemActions.ToggleDesktopIcons() switch
        {
            true => Strings.SysIconsShown,
            false => Strings.SysIconsHidden,
            null => Strings.SysNoDesktop,
        }));
        actions.Register(OrbitAction.HiddenFiles, () => Toast.Show(SystemActions.ToggleHiddenFiles() ? Strings.SysHiddenShown : Strings.SysHiddenHidden));
        actions.Register(OrbitAction.EmptyTrash, EmptyRecycleBin);
        actions.Register(OrbitAction.EjectAll, () => _ = EjectAllAsync());
    }

    private static void EmptyRecycleBin()
    {
        if (SystemActions.RecycleBinCount() is 0)
        {
            Toast.Show(Strings.SysRecycleAlreadyEmpty);
            return;
        }
        if (MessageBox.Show(Strings.SysRecycleConfirm, Strings.AppName, MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        Toast.Show(SystemActions.EmptyRecycleBin() ? Strings.SysRecycleEmptied : Strings.SysRecycleFailed);
    }

    /// <summary>The shell's verbs want a single-threaded apartment, and ejecting takes a moment: a thread of its own.</summary>
    private static Task<T> OnSta<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>();
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(work());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static async Task EjectAllAsync()
    {
        (int ejected, int failed) result;
        try
        {
            result = await OnSta(SystemActions.EjectAll);
        }
        catch (Exception e)
        {
            Log.Error("Eject all failed", e);
            Toast.Show(Strings.SomethingFailed);
            return;
        }
        var (ejected, failed) = result;
        Log.Info($"Eject all: {ejected} ejected, {failed} in use");
        if (ejected == 0 && failed == 0) Toast.Show(Strings.SysNothingToEject);
        else if (failed == 0) Toast.Show(Strings.SysEjected(ejected));
        else Toast.Show(Strings.SysInUse(failed), ejected > 0 ? Strings.SysEjected(ejected) : null);
    }
}
