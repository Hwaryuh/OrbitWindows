using Orbit.App.Design;
using Orbit.App.Features.Launcher;
using Orbit.Core;
using Orbit.Core.Actions;
using Orbit.Core.Input;
using Orbit.Core.Launcher;

namespace Orbit.App.Features;

/// <summary>
/// The launcher (Orbit: Modules/Launcher): its shortcut (Alt+Space, docs/plan/04-shortcuts.md)
/// and its action open it, or close it when open. The app list is read once at start and again
/// when the launcher opens and the list is a minute old.
/// </summary>
internal sealed class LauncherFeature : IFeature
{
    private int _hotkey;
    private AppServices? _services;
    private ShellWorker? _shell;
    private AppIndex? _apps;
    private WeatherService? _weather;
    private LauncherModel? _model;
    private LauncherWindow? _window;

    public void Start(AppServices services)
    {
        _services = services;
        _shell = new ShellWorker();
        _apps = new AppIndex(_shell);
        _apps.RefreshIfOlderThan(TimeSpan.Zero);
        _weather = new WeatherService();
        _model = new LauncherModel(services, _apps, new Frecency(services.Preferences),
            new ExchangeRates(services.Preferences), _weather, new FileSearch());
        _hotkey = services.Shortcuts.Declare(new Hotkey(Modifiers.Alt, HotkeyMatcher.VkSpace), OrbitAction.Launcher);
        services.Actions.Register(OrbitAction.Launcher, Toggle);
    }

    public void OnHook(HookEvent e)
    {
        if (e.Notice != HookNotice.HotkeyDown || e.Hotkey != _hotkey) return;
        Log.Info("Launcher shortcut pressed");
        Toggle();
    }

    public void Dispose()
    {
        _window?.Close();
        _shell?.Dispose();
        _weather?.Dispose();
    }

    private void Toggle()
    {
        if (_window is { IsVisible: true })
        {
            _window.Dismiss();
            return;
        }
        // Built once and kept; built again when Windows switched between light and dark.
        var palette = Palette.Current();
        if (_window is null || _window.Palette.Dark != palette.Dark)
        {
            _window?.Close();
            _window = new LauncherWindow(_model!, _apps!, palette);
        }
        _window.Present();
    }
}
