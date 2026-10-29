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
    /// <summary>A drag: send the press again, marked as Orbit's, now. The real release then goes to the app.</summary>
    ReplayPress = 64,
}

/// <summary>
/// Hold a mouse button to open the ring (Orbit: Modules/Ring/MouseTrigger.swift). The press is
/// swallowed; held for <see cref="HoldDelay"/> it opens the ring, released sooner it is replayed
/// as a click. Moved <see cref="DragDistance"/> or more before that, it was a drag (a canvas pan in
/// Figma, autoscroll by holding in a browser): the press is replayed and the rest of the drag is the
/// app's. Pure: the hook thread feeds it events and the timer, the tests feed it times.
/// </summary>
public sealed class MouseHoldTrigger
{
    /// <summary>Orbit's holdDelay (MouseTrigger.swift:14).</summary>
    public static readonly TimeSpan HoldDelay = TimeSpan.FromMilliseconds(180);

    /// <summary>How far the pointer may move during the hold, in pixels at 100 %, before the press is a drag.</summary>
    public const int DragDistance = 8;

    /// <summary>DragLost: the press couldn't be sent again, so the app must not get the release either.</summary>
    private enum State { Idle, Pressed, Open, Dragging, DragLost }

    private State _state;
    private long _dragSquared;

    public MouseButton Button { get; set; } = MouseButton.Middle;
    public bool Enabled { get; set; } = true;
    public int PressX { get; private set; }
    public int PressY { get; private set; }

    /// <summary>The button of the press in progress: <see cref="Button"/> may change before it goes up.</summary>
    public MouseButton PressedButton { get; private set; }

    public bool IsOpen => _state == State.Open;

    /// <summary>Waiting for the hold or a move: only then do moves matter.</summary>
    public bool IsPressed => _state == State.Pressed;

    /// <param name="dragDistance">In physical pixels: <see cref="DragDistance"/> scaled to the monitor.</param>
    public TriggerAction Down(MouseButton button, int x, int y, int dragDistance = DragDistance)
    {
        if (!Enabled || button != Button || _state != State.Idle) return TriggerAction.None;
        _state = State.Pressed;
        PressedButton = button;
        PressX = x;
        PressY = y;
        _dragSquared = (long)dragDistance * dragDistance;
        return TriggerAction.Swallow | TriggerAction.StartTimer;
    }

    public TriggerAction Move(int x, int y)
    {
        if (_state != State.Pressed) return TriggerAction.None;
        long dx = x - PressX, dy = y - PressY;
        if (dx * dx + dy * dy < _dragSquared) return TriggerAction.None;
        _state = State.Dragging;
        return TriggerAction.StopTimer | TriggerAction.ReplayPress;
    }

    public TriggerAction Up(MouseButton button)
    {
        if (_state == State.Idle || button != PressedButton) return TriggerAction.None;
        var state = _state;
        _state = State.Idle;
        return state switch
        {
            State.Pressed => TriggerAction.Swallow | TriggerAction.StopTimer | TriggerAction.ReplayClick,
            State.Open => TriggerAction.Swallow | TriggerAction.Release,
            State.DragLost => TriggerAction.Swallow,
            // The app got the press back, so it gets the release too.
            _ => TriggerAction.None,
        };
    }

    /// <summary>The replayed press of a drag wasn't sent.</summary>
    public void PressNotReplayed()
    {
        if (_state == State.Dragging) _state = State.DragLost;
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
