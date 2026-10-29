using Orbit.App.Features.Ring;
using Orbit.Core;
using Orbit.Core.Actions;
using Orbit.Core.Input;
using Orbit.Core.Ring;
using Orbit.Core.Settings;

namespace Orbit.App.Features;

/// <summary>
/// The ring (Orbit: OrbitRing.swift, MouseTrigger.swift), held open by a mouse button or by its
/// shortcut (Ctrl+Alt+Space, docs/plan/04-shortcuts.md). Opening it again while open closes it.
/// While it is open, Esc and presses outside it close it (the hooks report both).
/// </summary>
internal sealed class RingFeature : IFeature
{
    /// <summary>Captures wait for the ring to fade before the screen freezes; everything else runs at once.</summary>
    private static readonly TimeSpan CaptureDelay = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan ActionDelay = TimeSpan.FromMilliseconds(20);

    /// <summary>Holding it opens the ring; the ring page shows it.</summary>
    public static readonly Hotkey Shortcut = new(Modifiers.Ctrl | Modifiers.Alt, HotkeyMatcher.VkSpace);

    private AppServices? _services;
    private int _hotkey;
    private RingWindow? _ring;
    private bool _heldByMouse;

    public void Start(AppServices services)
    {
        _services = services;
        _hotkey = services.AddHotkey(Shortcut);
        ApplyMouseTrigger();
        services.Preferences.Changed += key =>
        {
            if (key is Pref.RingMouse or Pref.RingMouseButton) ApplyMouseTrigger();
        };
    }

    public void OnHook(HookEvent e)
    {
        switch (e.Notice)
        {
            case HookNotice.RingOpen:
                Toggle(byMouse: true);
                break;
            case HookNotice.HotkeyDown when e.Hotkey == _hotkey:
                Toggle(byMouse: false);
                break;
            // Only the release of what opened this ring: a key let go late must not end a ring the mouse opened.
            case HookNotice.RingRelease when _heldByMouse:
            case HookNotice.HotkeyUp when e.Hotkey == _hotkey && !_heldByMouse:
                _ring?.Release();
                break;
            case HookNotice.Click:
                _ring?.PressedAt(e.X, e.Y);
                break;
            case HookNotice.Escape:
                _ring?.Cancel();
                break;
            case HookNotice.HooksReinstalled:
                // The mouse trigger's press was forgotten; a ring held by the mouse would never get its release.
                if (_heldByMouse) _ring?.Cancel();
                break;
        }
    }

    public void Dispose() => _ring?.Cancel();

    private void Toggle(bool byMouse)
    {
        if (_ring is not null)
        {
            _ring.Cancel();
            return;
        }
        _heldByMouse = byMouse;
        var services = _services!;
        var slots = RingSlots.Parse(services.Preferences.Get<string[]>(Pref.RingSlots));
        var dwell = TimeSpan.FromSeconds(services.Preferences.Get<double>(Pref.RingDwell));
        var ring = RingWindow.Open(slots, hold: true, dwell, services.Preferences.Get<bool>(Pref.RingHover));
        ring.Finished += result => OnFinished(ring, slots, result);
        _ring = ring;
        services.Hooks.ReportClicks = true;
        services.Hooks.CaptureEscape = true;
    }

    private void OnFinished(RingWindow ring, IReadOnlyList<OrbitAction> slots, RingEvent result)
    {
        if (_ring == ring) _ring = null;
        var services = _services!;
        services.Hooks.ReportClicks = false;
        services.Hooks.CaptureEscape = false;
        if (result.Kind != RingEventKind.Fire) return;

        var action = slots[result.Index];
        var delay = action.Group() == ActionGroup.Capture ? CaptureDelay : ActionDelay;
        Later.Run(delay, () => services.Actions.Perform(action));
    }

    private void ApplyMouseTrigger()
    {
        var prefs = _services!.Preferences;
        var trigger = _services.Hooks.RingTrigger;
        trigger.Enabled = prefs.Get<bool>(Pref.RingMouse);
        trigger.Button = prefs.Get<string>(Pref.RingMouseButton) switch
        {
            "x1" => MouseButton.X1,
            "x2" => MouseButton.X2,
            _ => MouseButton.Middle,
        };
        Log.Info($"Ring mouse trigger {(trigger.Enabled ? trigger.Button.ToString() : "off")}");
    }
}
