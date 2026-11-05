using System.Reflection;
using System.Windows;
using Orbit.App.Overlays;
using Orbit.Core;
using Orbit.Core.Actions;
using Velopack;
using Velopack.Sources;

namespace Orbit.App.Features;

/// <summary>
/// Updates through Velopack from the GitHub Releases of the repository the build came from. CI writes
/// that address into the build (<c>UpdateRepo</c>), so a build without it, or one that isn't installed,
/// can't check and says so. Nothing is installed without a yes. The update waits for Orbit to quit
/// the usual way, so notes get saved and a running recording is kept.
/// </summary>
internal sealed class UpdateFeature : IFeature
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(30);

    private AppServices? _services;
    private bool _busy;

    public void Start(AppServices services)
    {
        _services = services;
        services.Actions.Register(OrbitAction.CheckUpdates, () => _ = CheckAsync(manual: true));
        if (Manager() is { IsInstalled: true }) _ = CheckLaterAsync();
    }

    private async Task CheckLaterAsync()
    {
        await Task.Delay(FirstCheck);
        await Application.Current.Dispatcher.InvokeAsync(() => CheckAsync(manual: false)).Task.Unwrap();
    }

    private static UpdateManager? Manager()
    {
        var repo = typeof(UpdateFeature).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "UpdateRepo")?.Value;
        return string.IsNullOrWhiteSpace(repo) ? null : new UpdateManager(new GithubSource(repo, null, false));
    }

    private async Task CheckAsync(bool manual)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (Manager() is not { IsInstalled: true } manager)
            {
                if (manual) Toast.Show(Strings.UpdateNotSupported);
                return;
            }
            if (await manager.CheckForUpdatesAsync() is not { } update)
            {
                if (manual) Toast.Show(Strings.UpdateNone);
                return;
            }
            string version = update.TargetFullRelease.Version.ToString();
            if (!manual)
            {
                // The silent check only tells; installing starts from the action.
                Toast.Show(Strings.UpdateAvailable(version));
                return;
            }
            if (_services!.Recording?.IsRecording == true)
            {
                Toast.Show(Strings.UpdateAfterRecording);
                return;
            }
            if (MessageBox.Show(Strings.UpdateConfirm(version), Strings.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await manager.DownloadUpdatesAsync(update);
            manager.WaitExitThenApplyUpdates(update.TargetFullRelease);
            Application.Current.Shutdown();
        }
        catch (Exception e)
        {
            Log.Warn($"Update check failed: {e.GetType().Name}");
            if (manual) Toast.Show(Strings.UpdateFailed);
        }
        finally
        {
            _busy = false;
        }
    }
}
