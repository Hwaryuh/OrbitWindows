using Orbit.Core.Input;

namespace Orbit.Tests;

public class MouseHoldTriggerTests
{
    [Fact]
    public void QuickClickIsSwallowedAndReplayed()
    {
        var trigger = new MouseHoldTrigger();
        Assert.Equal(TriggerAction.Swallow | TriggerAction.StartTimer, trigger.Down(MouseButton.Middle, 10, 20));
        Assert.Equal(TriggerAction.Swallow | TriggerAction.StopTimer | TriggerAction.ReplayClick, trigger.Up(MouseButton.Middle));
        Assert.False(trigger.IsOpen);
    }

    [Fact]
    public void HoldOpensAtThePressAndReleaseCloses()
    {
        var trigger = new MouseHoldTrigger();
        trigger.Down(MouseButton.Middle, -500, 300);
        Assert.True((trigger.TimerElapsed() & TriggerAction.Open) != 0);
        Assert.True(trigger.IsOpen);
        Assert.Equal((-500, 300), (trigger.PressX, trigger.PressY));
        Assert.Equal(TriggerAction.Swallow | TriggerAction.Release, trigger.Up(MouseButton.Middle));
        Assert.False(trigger.IsOpen);
    }

    [Fact]
    public void OtherButtonsPassThrough()
    {
        var trigger = new MouseHoldTrigger();
        Assert.Equal(TriggerAction.None, trigger.Down(MouseButton.Left, 0, 0));
        Assert.Equal(TriggerAction.None, trigger.Up(MouseButton.Right));
    }

    [Fact]
    public void ConfiguredSideButtonTriggers()
    {
        var trigger = new MouseHoldTrigger { Button = MouseButton.X1 };
        Assert.Equal(TriggerAction.None, trigger.Down(MouseButton.Middle, 0, 0));
        Assert.NotEqual(TriggerAction.None, trigger.Down(MouseButton.X1, 0, 0));
    }

    [Fact]
    public void DisabledTriggerPassesEverything()
    {
        var trigger = new MouseHoldTrigger { Enabled = false };
        Assert.Equal(TriggerAction.None, trigger.Down(MouseButton.Middle, 0, 0));
        Assert.Equal(TriggerAction.None, trigger.Up(MouseButton.Middle));
    }

    [Fact]
    public void LateTimerAfterReleaseDoesNothing()
    {
        var trigger = new MouseHoldTrigger();
        trigger.Down(MouseButton.Middle, 0, 0);
        trigger.Up(MouseButton.Middle);
        Assert.Equal(TriggerAction.StopTimer, trigger.TimerElapsed());
        Assert.False(trigger.IsOpen);
    }

    [Fact]
    public void SecondPressWhileOpenIsIgnored()
    {
        var trigger = new MouseHoldTrigger();
        trigger.Down(MouseButton.Middle, 0, 0);
        trigger.TimerElapsed();
        Assert.Equal(TriggerAction.None, trigger.Down(MouseButton.Middle, 5, 5));
        Assert.True(trigger.IsOpen);
    }

    [Fact]
    public void SmallMoveDuringTheHoldStillOpens()
    {
        var trigger = new MouseHoldTrigger();
        trigger.Down(MouseButton.Middle, 100, 100, dragDistance: 10);
        Assert.Equal(TriggerAction.None, trigger.Move(106, 107));
        Assert.True((trigger.TimerElapsed() & TriggerAction.Open) != 0);
    }

    [Fact]
    public void DragReplaysThePressAndPassesTheRelease()
    {
        var trigger = new MouseHoldTrigger();
        trigger.Down(MouseButton.Middle, 100, 100, dragDistance: 10);
        Assert.Equal(TriggerAction.StopTimer | TriggerAction.ReplayPress, trigger.Move(106, 108));
        Assert.Equal(TriggerAction.None, trigger.Move(200, 200));
        Assert.Equal(TriggerAction.StopTimer, trigger.TimerElapsed());
        Assert.False(trigger.IsOpen);
        Assert.Equal(TriggerAction.None, trigger.Up(MouseButton.Middle));
        Assert.NotEqual(TriggerAction.None, trigger.Down(MouseButton.Middle, 0, 0));
    }

    [Fact]
    public void DragWhosePressWasNotReplayedSwallowsTheRelease()
    {
        var trigger = new MouseHoldTrigger();
        trigger.Down(MouseButton.Middle, 0, 0);
        trigger.Move(50, 0);
        trigger.PressNotReplayed();
        Assert.Equal(TriggerAction.Swallow, trigger.Up(MouseButton.Middle));
        Assert.NotEqual(TriggerAction.None, trigger.Down(MouseButton.Middle, 0, 0));
    }

    [Fact]
    public void MoveAfterTheRingOpenedChangesNothing()
    {
        var trigger = new MouseHoldTrigger();
        trigger.Down(MouseButton.Middle, 0, 0);
        trigger.TimerElapsed();
        Assert.Equal(TriggerAction.None, trigger.Move(500, 500));
        Assert.Equal(TriggerAction.Swallow | TriggerAction.Release, trigger.Up(MouseButton.Middle));
    }

    [Fact]
    public void ButtonChangedDuringAPressEndsWithThePressedButton()
    {
        var trigger = new MouseHoldTrigger();
        trigger.Down(MouseButton.Middle, 0, 0);
        trigger.Button = MouseButton.X1;
        Assert.Equal(TriggerAction.None, trigger.Up(MouseButton.X1));
        Assert.Equal(TriggerAction.Swallow | TriggerAction.StopTimer | TriggerAction.ReplayClick, trigger.Up(MouseButton.Middle));
    }

    [Fact]
    public void ResetDuringADragLetsTheReleasePass()
    {
        var trigger = new MouseHoldTrigger();
        trigger.Down(MouseButton.Middle, 0, 0);
        trigger.Move(50, 0);
        trigger.Reset();
        Assert.Equal(TriggerAction.None, trigger.Up(MouseButton.Middle));
    }

    [Fact]
    public void ResetForgetsAPress()
    {
        var trigger = new MouseHoldTrigger();
        trigger.Down(MouseButton.Middle, 0, 0);
        trigger.Reset();
        Assert.Equal(TriggerAction.None, trigger.Up(MouseButton.Middle));
    }
}
