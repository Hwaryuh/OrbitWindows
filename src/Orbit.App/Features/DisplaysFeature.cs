using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Orbit.App.Overlays;
using Orbit.App.Settings;
using Orbit.Core.Actions;
using Orbit.Core.Display;
using Orbit.Core.Displays;
using Orbit.Core.Settings;

namespace Orbit.App.Features;

/// <summary>
/// Displays (Orbit: Modules/Displays): every screen's brightness through <see cref="DisplayManager"/>,
/// the brightness actions with their HUD, and the Displays page. The screens are asked again a
/// moment after Windows reports a change (monitors take time to answer once they appear).
/// </summary>
internal sealed class DisplaysFeature : IFeature
{
    private readonly DispatcherTimer _settle = new() { Interval = TimeSpan.FromSeconds(1.2) };
    private AppServices? _services;
    private DisplayManager? _manager;

    public void Start(AppServices services)
    {
        _services = services;
        var manager = _manager = new DisplayManager(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        services.Displays = manager;
        _ = manager.Refresh();
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            _ = manager.Refresh();
        };
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
        services.Actions.Register(OrbitAction.Displays, () => SettingsWindow.ShowOrActivate(services, "displays"));
        services.Actions.Register(OrbitAction.BrightnessUp, () => _ = Step(up: true));
        services.Actions.Register(OrbitAction.BrightnessDown, () => _ = Step(up: false));
    }

    private void OnDisplaysChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _settle.Stop();
            _settle.Start();
        });

    /// <summary>One step brighter or dimmer on the chosen screens, from each one's latest level.</summary>
    private async Task Step(bool up)
    {
        if (_manager is not { } manager || _services is null) return;
        if (manager.Displays.Count == 0) await manager.Refresh();
        var targets = Targets(manager, _services.Preferences.Get<string>(Pref.DisplaysTarget));
        if (targets.Count == 0)
        {
            Toast.Show(Strings.DisplaysNothingToSet);
            return;
        }
        foreach (var display in targets) manager.SetBrightness(display, DisplayLevels.Stepped(display.Brightness, up));
        ShowLevel(_services.Preferences, targets);
    }

    /// <summary>The level bar on the first changed screen, a monitor before the laptop's own, if wanted.</summary>
    public static void ShowLevel(Preferences preferences, IReadOnlyList<ManagedDisplay> changed)
    {
        if (!preferences.Get<bool>(Pref.DisplaysHud)) return;
        var shown = changed.FirstOrDefault(d => !d.IsBuiltIn) ?? changed.FirstOrDefault();
        if (shown is not null) LevelHud.Show(shown.Monitor, shown.Brightness);
    }

    private static List<ManagedDisplay> Targets(DisplayManager manager, string target)
    {
        var usable = manager.Displays.Where(d => d.CanSetBrightness);
        return target switch
        {
            "all" => usable.ToList(),
            "main" => usable.Where(d => d.IsPrimary).ToList(),
            _ => usable.Where(d => d.Monitor == UnderPointer()).ToList(),
        };
    }

    private static nint UnderPointer()
    {
        var (x, y) = Monitors.Cursor();
        return Monitors.FromPoint(x, y)?.Handle ?? 0;
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        _settle.Stop();
        _manager?.Dispose();
    }
}
