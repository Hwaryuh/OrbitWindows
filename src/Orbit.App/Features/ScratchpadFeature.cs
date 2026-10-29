using Orbit.App.Features.Scratchpad;
using Orbit.Core.Actions;
using Orbit.Core.Scratchpad;

namespace Orbit.App.Features;

/// <summary>
/// The scratchpad (behavior after Vorssaint's Scratchpad; Orbit has none): its action opens the
/// pad, focuses it when it shows but isn't in front, and hides it when it is. The file is read
/// the first time the pad opens; what waits is written when Orbit quits.
/// </summary>
internal sealed class ScratchpadFeature : IFeature
{
    private AppServices? _services;
    private ScratchpadWindow? _window;

    public void Start(AppServices services)
    {
        _services = services;
        services.Actions.Register(OrbitAction.Scratchpad, Toggle);
    }

    private void Toggle()
    {
        if (_services is null) return;
        if (_window is null)
        {
            var store = new ScratchpadStore(ScratchpadStore.DefaultPath);
            var document = store.Load(Strings.ScratchpadTabName, DateTimeOffset.Now);
            _window = new ScratchpadWindow(store, document, _services.Preferences);
        }
        if (_window.IsVisible && _window.IsActive) _window.HidePad();
        else _window.Present();
    }

    public void Dispose() => _window?.Quit();
}
