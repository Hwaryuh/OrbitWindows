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
    public void ResetForgetsAPress()
    {
        var trigger = new MouseHoldTrigger();
        trigger.Down(MouseButton.Middle, 0, 0);
        trigger.Reset();
        Assert.Equal(TriggerAction.None, trigger.Up(MouseButton.Middle));
    }
}
