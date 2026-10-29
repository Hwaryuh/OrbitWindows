using Orbit.Core;
using Orbit.Core.Actions;
using Orbit.Core.Input;

namespace Orbit.App.Features;

/// <summary>
/// The launcher's shortcut (Alt+Space, docs/plan/04-shortcuts.md). The launcher itself comes in
/// M1 and registers <see cref="OrbitAction.Launcher"/>; until then the action says it isn't ready.
/// </summary>
internal sealed class LauncherFeature : IFeature
{
    private int _hotkey;
    private ActionRunner? _actions;

    public void Start(AppServices services)
    {
        _actions = services.Actions;
        _hotkey = services.AddHotkey(new Hotkey(Modifiers.Alt, HotkeyMatcher.VkSpace));
    }

    public void OnHook(HookEvent e)
    {
        if (e.Notice != HookNotice.HotkeyDown || e.Hotkey != _hotkey) return;
        Log.Info("Launcher shortcut pressed");
        _actions?.Perform(OrbitAction.Launcher);
    }
}
