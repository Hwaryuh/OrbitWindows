using Orbit.App.Settings;
using Orbit.Core.Actions;

namespace Orbit.App.Features;

/// <summary>The settings window, as an action (the tray, the ring and the launcher open it).</summary>
internal sealed class SettingsFeature : IFeature
{
    public void Start(AppServices services) =>
        services.Actions.Register(OrbitAction.Settings, () => SettingsWindow.ShowOrActivate(services));
}
