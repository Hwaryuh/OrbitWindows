namespace Orbit.Core.Input;

/// <summary>What the hook must do with a mouse event, and what follows from it.</summary>
[Flags]
public enum TriggerAction
{
    None = 0,
    /// <summary>Don't pass the event on to the app.</summary>
    Swallow = 1,
    /// <summary>Start the hold timer (<see cref="MouseHoldTrigger.HoldDelay"/>).</summary>
    StartTimer = 2,
    StopTimer = 4,
    /// <summary>The button was held long enough: open the ring at the press position.</summary>
    Open = 8,
    /// <summary>The button went up while the ring was open.</summary>
    Release = 16,
    /// <summary>A quick click: send the press and release again, marked as Orbit's, so the app gets its click.</summary>
    ReplayClick = 32,
}

/// <summary>
/// Hold a mouse button to open the ring (Orbit: Modules/Ring/MouseTrigger.swift). The press is
/// swallowed; held for <see cref="HoldDelay"/> it opens the ring, released sooner it is replayed
/// as a click. Pure: the hook thread feeds it events and the timer, the tests feed it times.
/// </summary>
public sealed class MouseHoldTrigger
{
    /// <summary>Orbit's holdDelay (MouseTrigger.swift:14).</summary>
    public static readonly TimeSpan HoldDelay = TimeSpan.FromMilliseconds(180);

    private enum State { Idle, Pressed, Open }

    private State _state;

    public MouseButton Button { get; set; } = MouseButton.Middle;
    public bool Enabled { get; set; } = true;
    public int PressX { get; private set; }
    public int PressY { get; private set; }

    public bool IsOpen => _state == State.Open;

    public TriggerAction Down(MouseButton button, int x, int y)
    {
        if (!Enabled || button != Button || _state != State.Idle) return TriggerAction.None;
        _state = State.Pressed;
        PressX = x;
        PressY = y;
        return TriggerAction.Swallow | TriggerAction.StartTimer;
    }

    public TriggerAction Up(MouseButton button)
    {
        if (button != Button) return TriggerAction.None;
        switch (_state)
        {
            case State.Pressed:
                _state = State.Idle;
                return TriggerAction.Swallow | TriggerAction.StopTimer | TriggerAction.ReplayClick;
            case State.Open:
                _state = State.Idle;
                return TriggerAction.Swallow | TriggerAction.Release;
            default:
                return TriggerAction.None;
        }
    }

    public TriggerAction TimerElapsed()
    {
        if (_state != State.Pressed) return TriggerAction.StopTimer;
        _state = State.Open;
        return TriggerAction.StopTimer | TriggerAction.Open;
    }

    /// <summary>Forget a press, as after a lock screen or a hook that was removed.</summary>
    public void Reset() => _state = State.Idle;
}

public enum MouseButton { None, Left, Right, Middle, X1, X2 }
