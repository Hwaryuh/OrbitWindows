using Orbit.App.Overlays;
using Orbit.Core.Display;
using Orbit.Core.Input;

namespace Orbit.App.Features;

/// <summary>
/// The ring: held open by the mouse trigger or by its shortcut (Ctrl+Alt+Space,
/// docs/plan/04-shortcuts.md). For now the M0 test ring; M1 puts Orbit's ring here.
/// </summary>
internal sealed class RingFeature : IFeature
{
    private int _hotkey;
    private TestRingWindow? _ring;

    public void Start(AppServices services) =>
        _hotkey = services.AddHotkey(new Hotkey(Modifiers.Ctrl | Modifiers.Alt, HotkeyMatcher.VkSpace));

    public void OnHook(HookEvent e)
    {
        switch (e.Notice)
        {
            case HookNotice.RingOpen:
                Open(e.X, e.Y);
                break;
            case HookNotice.HotkeyDown when e.Hotkey == _hotkey:
                var (x, y) = Monitors.Cursor();
                Open(x, y);
                break;
            case HookNotice.RingRelease:
            case HookNotice.HotkeyUp when e.Hotkey == _hotkey:
            case HookNotice.HooksReinstalled:
                Close();
                break;
        }
    }

    public void Dispose() => Close();

    private void Open(int x, int y)
    {
        Close();
        _ring = new TestRingWindow();
        _ring.Open(x, y);
    }

    private void Close()
    {
        _ring?.Release();
        _ring = null;
    }
}
