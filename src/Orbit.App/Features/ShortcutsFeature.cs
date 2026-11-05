using System.Windows.Threading;
using Orbit.Core.Input;

namespace Orbit.App.Features;

/// <summary>
/// Runs the actions that have keys but no feature handling them, and records a key for the
/// Shortcuts page. A recording is one session, and every way out of it clears the matcher's flag:
/// a key, Dispose, or the time limit, so no path can leave every shortcut dead.
/// </summary>
internal sealed class ShortcutsFeature : IFeature
{
    private static readonly TimeSpan Longest = TimeSpan.FromSeconds(15);

    private AppServices? _services;
    private Session? _session;

    public void Start(AppServices services)
    {
        _services = services;
        services.ShortcutRecorder = this;
    }

    public void OnHook(HookEvent e)
    {
        if (e.Notice == HookNotice.HooksReinstalled)
        {
            _session?.Dispose();
            return;
        }
        if (e.Notice != HookNotice.HotkeyDown) return;
        if (HotkeyMatcher.Recorded(e.Hotkey) is { } key) _session?.Deliver(key);
        else if (_services!.Shortcuts.ActionFor(e.Hotkey) is { } action) _services.Actions.Perform(action);
    }

    /// <summary>Until the session ends, the next key goes to <paramref name="got"/> and no shortcut runs. <paramref name="ended"/> follows every end.</summary>
    public IDisposable BeginRecording(Action<Hotkey> got, Action ended)
    {
        _session?.Dispose();
        _session = new Session(this, got, ended);
        return _session;
    }

    public void Dispose() => _session?.Dispose();

    private sealed class Session : IDisposable
    {
        private readonly ShortcutsFeature _owner;
        private readonly Action<Hotkey> _got;
        private readonly Action _ended;
        private readonly DispatcherTimer _limit = new() { Interval = Longest };
        private bool _done;

        public Session(ShortcutsFeature owner, Action<Hotkey> got, Action ended)
        {
            _owner = owner;
            _got = got;
            _ended = ended;
            _limit.Tick += (_, _) => Dispose();
            _limit.Start();
            owner._services!.Hooks.Hotkeys.Recording = true;
        }

        public void Deliver(Hotkey key)
        {
            if (_done) return;
            End();
            try
            {
                _got(key);
            }
            finally
            {
                _ended();
            }
        }

        public void Dispose()
        {
            if (_done) return;
            End();
            _ended();
        }

        private void End()
        {
            _done = true;
            _limit.Stop();
            _owner._services!.Hooks.Hotkeys.Recording = false;
            if (_owner._session == this) _owner._session = null;
        }
    }
}
