using Orbit.Core.Input;

namespace Orbit.Core.Typing;

/// <summary>
/// The typing thread and the way keys reach it: the hook pushes events, this thread works them out and
/// sends them, in order. If the engine or a slow app holds a key up too long, the queue goes out as
/// typed from the watchdog and the late answer is thrown away, so a stall can't eat typing.
/// </summary>
public sealed class TypingWorker : IDisposable
{
    private const int Capacity = 1024;
    private const long StallMs = 250;
    private const int TickMs = 100;
    private const long RetryMs = 150;
    private const int Retries = 3;

    private readonly Func<ITypingEngine> _makeEngine;
    private readonly ITypingHost _host;
    private readonly AutoResetEvent _signal = new(false);
    private readonly Lock _ringLock = new();
    private readonly Lock _sendLock = new();
    private readonly KeyEvent[] _ring = new KeyEvent[Capacity];
    private int _head;
    private int _count;

    private KeyEvent _current;
    private bool _hasCurrent;
    private long _currentSince;
    private long _epoch;
    private bool _stalled;

    private TypingCore? _core;
    private Thread? _thread;
    private Timer? _watchdog;
    private volatile bool _stopping;
    private volatile bool _forget;
    private volatile bool _lookFocus;
    private volatile bool _lookFront;
    private volatile bool _enabled;
    private HeldModifiers _held;
    private long _retryAt;
    private int _retriesLeft = Retries;

    public TypingWorker(Func<ITypingEngine> makeEngine, ITypingHost host)
    {
        _makeEngine = makeEngine;
        _host = host;
    }

    public TypingGate Gate { get; } = new();

    /// <summary>Why keys are left alone now.</summary>
    public TypingPause Pause => _core?.Pause ?? TypingPause.Off;

    /// <summary>The engine couldn't be loaded: nothing will ever be taken.</summary>
    public bool Failed { get; private set; }

    /// <summary>Raised on the typing thread.</summary>
    public event Action? PauseChanged;

    public void Start(bool enabled)
    {
        _enabled = enabled;
        _lookFront = true;
        // Big enough for the Swift runtime, which is used to a main thread's stack.
        _thread = new Thread(Run, 16 * 1024 * 1024) { Name = "Orbit typing", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _signal.Set();
        _watchdog = new Timer(_ =>
        {
            long now = Environment.TickCount64;
            CheckStall(now);
            CheckRetry(now);
        }, null, TickMs, TickMs);
    }

    public void Dispose()
    {
        _stopping = true;
        Gate.Active = false;
        // Keys already kept from the app still have to reach it.
        lock (_sendLock) SendAllAsTyped();
        _signal.Set();
        _thread?.Join(TimeSpan.FromSeconds(1));
        _watchdog?.Dispose();
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        Interlocked.Exchange(ref _retryAt, 0);
        RetriesFromStart();
        _lookFront = true;
        _signal.Set();
    }

    /// <summary>Focus moved inside a window. Any thread.</summary>
    public void FocusMoved()
    {
        RetriesFromStart();
        _lookFocus = true;
        _signal.Set();
    }

    /// <summary>Another program came to the front. Any thread.</summary>
    public void FrontMoved()
    {
        RetriesFromStart();
        _lookFront = true;
        _signal.Set();
    }

    /// <summary>The box may have changed without a key: a mouse press, another Orbit feature's keys. Any thread, no allocation.</summary>
    public void BoxMayHaveChanged()
    {
        if (!Gate.HasWord) return;
        _forget = true;
        _signal.Set();
    }

    /// <summary>On the hook thread, with no allocation. True keeps the event from the app.</summary>
    public bool KeyArrived(uint vk, uint scan, bool extended, bool down, bool capsLock)
    {
        _held.Note(vk, down);
        var action = Gate.Decide(vk, down, _held.Shortcut);
        if (action == GateAction.Drop) return true;
        if (action == GateAction.Pass) return false;

        var key = new KeyEvent(vk, (ushort)scan, down, extended, _held.Shift, _held.Ctrl, _held.Alt, _held.Win, capsLock, Environment.TickCount64);
        lock (_ringLock)
        {
            if (_count < Capacity)
            {
                _ring[(_head + _count) % Capacity] = key;
                _count++;
                _signal.Set();
                return true;
            }
        }
        // Full can only mean the watchdog is dead too. Out of order beats lost.
        Gate.Finished();
        if (down) Gate.SentOn(vk);
        return false;
    }

    private void Run()
    {
        try
        {
            _core = new TypingCore(_makeEngine(), _host, Gate);
            _core.PauseChanged += () => PauseChanged?.Invoke();
        }
        catch (Exception e)
        {
            // Native loading fails in many ways, and every one of them means the same here.
            Log.Error("The typing engine could not be started", e);
            Failed = true;
            PauseChanged?.Invoke();
            return;
        }

        while (!_stopping)
        {
            _signal.WaitOne();
            RunLooks();
            RecoverFromStall();
            while (!_stopping && TryNext(out var key, out long epoch))
            {
                RunLooks();
                Work(key, epoch);
                RecoverFromStall();
            }
        }
        _host.ReleaseIme();
    }

    private void RunLooks()
    {
        var core = _core!;
        try
        {
            if (_forget)
            {
                _forget = false;
                core.Forget();
            }
            bool front = _lookFront;
            if (front || _lookFocus)
            {
                _lookFront = false;
                _lookFocus = false;
                core.Enabled = _enabled;
                core.Look(sameBoxKeepsWord: !front);
                // A read refused or too slow (the one before still running, just after Alt+Tab) decided
                // nothing, and no event may come to look again: a game or a canvas sends none.
                if (core.Unread && Interlocked.Decrement(ref _retriesLeft) >= 0)
                    Interlocked.Exchange(ref _retryAt, Environment.TickCount64 + RetryMs);
            }
        }
        catch (Exception e)
        {
            // Whatever it was, this thread has to live: keys wait on it. Only the type: messages can quote text.
            Log.Warn($"Typing couldn't look at the box ({e.GetType().Name})");
            Gate.Active = false;
        }
    }

    private bool TryNext(out KeyEvent key, out long epoch)
    {
        lock (_sendLock)
        {
            epoch = _epoch;
            if (!Pop(out key)) return false;
            _current = key;
            _hasCurrent = true;
            _currentSince = Environment.TickCount64;
            return true;
        }
    }

    private void Work(KeyEvent key, long epoch)
    {
        var core = _core!;
        KeyPlan plan;
        try
        {
            plan = core.Plan(key);
        }
        catch (Exception e)
        {
            // Whatever it was, the key still has to reach the app. Only the type: messages can quote text.
            Log.Warn($"Typing failed on a key ({e.GetType().Name})");
            TryForget(core);
            plan = key.Down ? KeyPlan.Down(key) : KeyPlan.Up(key);
        }

        lock (_sendLock)
        {
            // Otherwise the watchdog already sent this key as typed: the late answer is thrown away.
            if (epoch != _epoch) return;
            bool accepted = plan.Events.Length == 0 || _host.Send(plan.Events);
            if (plan.ReachesApp) Gate.SentOn(key.Vk);
            core.Sent(accepted);
            _hasCurrent = false;
            Gate.Finished();
        }
    }

    /// <summary>
    /// After a stall, wherever on this thread it hit: what was shown stays, the word starts over, and
    /// keys can be taken again.
    /// </summary>
    private void RecoverFromStall()
    {
        lock (_sendLock)
        {
            if (!_stalled) return;
            _stalled = false;
        }
        Log.Info("Typing is back after a stall");
        TryForget(_core!);
        _lookFront = true;
        RunLooks();
    }

    private static void TryForget(TypingCore core)
    {
        try
        {
            core.Forget();
        }
        catch (Exception e)
        {
            Log.Warn($"Typing couldn't let a word go ({e.GetType().Name})");
        }
    }

    /// <summary>Takes the queue over if a key has waited too long. Public for tests: the timer calls it.</summary>
    public void CheckStall(long now)
    {
        lock (_sendLock)
        {
            long since = _hasCurrent ? _currentSince : Oldest(now);
            if (!_stalled && now - since < StallMs) return;
            if (!_hasCurrent && QueueIsEmpty) return;

            if (!_stalled) Log.Warn("Typing stalled: keys are going out as typed");
            _stalled = true;
            Gate.Active = false;
            SendAllAsTyped();
        }
    }

    /// <summary>Something new to look at: unread boxes get their full count of looks again.</summary>
    private void RetriesFromStart() => Volatile.Write(ref _retriesLeft, Retries);

    /// <summary>Looks again at a box that couldn't be read. Public for tests: the timer calls it.</summary>
    public void CheckRetry(long now)
    {
        long at = Interlocked.Read(ref _retryAt);
        if (at == 0 || now < at || Interlocked.CompareExchange(ref _retryAt, 0, at) != at) return;
        _lookFocus = true;
        _signal.Set();
    }

    /// <summary>Under the send lock. Any answer still being worked out for these is thrown away.</summary>
    private void SendAllAsTyped()
    {
        _epoch++;
        if (_hasCurrent)
        {
            ReplayRaw(_current);
            _hasCurrent = false;
        }
        while (Pop(out var key)) ReplayRaw(key);
    }

    private void ReplayRaw(KeyEvent key)
    {
        if (key.Down)
        {
            _host.Send([KeyOutput.Raw(key)]);
            Gate.SentOn(key.Vk);
        }
        else if (!Gate.TakeUp(key.Vk))
        {
            _host.Send([KeyOutput.Raw(key)]);
        }
        Gate.Finished();
    }

    private bool QueueIsEmpty
    {
        get
        {
            lock (_ringLock) return _count == 0;
        }
    }

    private long Oldest(long now)
    {
        lock (_ringLock) return _count > 0 ? _ring[_head].At : now;
    }

    private bool Pop(out KeyEvent key)
    {
        lock (_ringLock)
        {
            if (_count == 0)
            {
                key = default;
                return false;
            }
            key = _ring[_head];
            _head = (_head + 1) % Capacity;
            _count--;
            return true;
        }
    }
}

/// <summary>
/// Modifiers down as the user holds them. Windows' own state lags behind, as keys kept in the queue
/// haven't reached it yet. On the hook thread only.
/// </summary>
public struct HeldModifiers
{
    private bool _leftShift, _rightShift, _leftCtrl, _rightCtrl, _leftAlt, _rightAlt, _leftWin, _rightWin;

    public readonly bool Shift => _leftShift || _rightShift;
    public readonly bool Ctrl => _leftCtrl || _rightCtrl;
    public readonly bool Alt => _leftAlt || _rightAlt;
    public readonly bool Win => _leftWin || _rightWin;
    public readonly bool Shortcut => Ctrl || Alt || Win;

    public void Note(uint vk, bool down)
    {
        switch (vk)
        {
            case HotkeyMatcher.VkShift or HotkeyMatcher.VkLShift: _leftShift = down; break;
            case HotkeyMatcher.VkRShift: _rightShift = down; break;
            case HotkeyMatcher.VkControl or HotkeyMatcher.VkLControl: _leftCtrl = down; break;
            case HotkeyMatcher.VkRControl: _rightCtrl = down; break;
            case HotkeyMatcher.VkMenu or HotkeyMatcher.VkLMenu: _leftAlt = down; break;
            case HotkeyMatcher.VkRMenu: _rightAlt = down; break;
            case HotkeyMatcher.VkLWin: _leftWin = down; break;
            case HotkeyMatcher.VkRWin: _rightWin = down; break;
        }
    }
}
