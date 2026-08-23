using VoiceCtrl.Core.Dictation;
using Xunit;

namespace VoiceCtrl.Core.Tests.Dictation;

public class DictationStateMachineTests
{
    [Fact]
    public void FirstToggleFromIdle_Starts()
    {
        var machine = new DictationStateMachine();

        Assert.Equal(DictationAction.Start, machine.RequestToggle());
    }

    [Fact]
    public void ToggleWhileRecording_Stops()
    {
        DictationStateMachine machine = Recording();

        Assert.Equal(DictationAction.Stop, machine.RequestToggle());
    }

    [Fact]
    public void ToggleAlternatesAcrossACompleteCycle()
    {
        var machine = new DictationStateMachine();

        Assert.Equal(DictationAction.Start, machine.RequestToggle());
        machine.SetRecording();
        machine.EndTransition();

        Assert.Equal(DictationAction.Stop, machine.RequestToggle());
        machine.SetProcessing();
        machine.Reset();
        machine.EndTransition();

        Assert.Equal(DictationAction.Start, machine.RequestToggle());
    }

    // The reason the gate exists at all: the hotkey can fire far faster than a start or stop
    // completes, where the old mouse-click path was limited by how fast a person could click.
    [Fact]
    public void SecondToggleBeforeTheFirstFinishes_IsIgnored()
    {
        var machine = new DictationStateMachine();

        Assert.Equal(DictationAction.Start, machine.RequestToggle());
        Assert.Equal(DictationAction.None, machine.RequestToggle());
        Assert.Equal(DictationAction.None, machine.RequestToggle());
    }

    [Fact]
    public void ToggleWhileProcessing_IsIgnored()
    {
        DictationStateMachine machine = Recording();
        machine.RequestToggle();
        machine.SetProcessing();

        Assert.Equal(DictationAction.None, machine.RequestToggle());
    }

    // An error message stays on screen for AutoHideDelayMs, and the hide that ends it is already
    // scheduled. A recording started during that gap would be hidden out from under the user.
    [Fact]
    public void ToggleWhileATransientMessageIsShowing_IsIgnored()
    {
        var machine = new DictationStateMachine();
        machine.RequestToggle();
        machine.SetError("Microphone unavailable");

        Assert.Equal(DictationAction.None, machine.RequestToggle());
    }

    [Fact]
    public void ToggleAfterATransientMessageHasCleared_StartsAgain()
    {
        var machine = new DictationStateMachine();
        machine.RequestToggle();
        machine.SetError("Microphone unavailable");
        machine.Reset();
        machine.EndTransition();

        Assert.Equal(DictationAction.Start, machine.RequestToggle());
    }

    [Fact]
    public void CancelWhileRecording_IsAccepted()
    {
        DictationStateMachine machine = Recording();

        Assert.True(machine.RequestCancel());
    }

    [Fact]
    public void CancelWhenIdle_IsRejected()
    {
        var machine = new DictationStateMachine();

        Assert.False(machine.RequestCancel());
    }

    [Fact]
    public void CancelWhileProcessing_IsRejected()
    {
        DictationStateMachine machine = Recording();
        machine.RequestToggle();
        machine.SetProcessing();

        Assert.False(machine.RequestCancel());
    }

    [Fact]
    public void SecondCancelBeforeTheFirstFinishes_IsRejected()
    {
        DictationStateMachine machine = Recording();

        Assert.True(machine.RequestCancel());
        Assert.False(machine.RequestCancel());
    }

    [Fact]
    public void ToggleDuringACancel_IsIgnored()
    {
        DictationStateMachine machine = Recording();
        machine.RequestCancel();

        Assert.Equal(DictationAction.None, machine.RequestToggle());
    }

    [Fact]
    public void ToggleAfterACancelCompletes_StartsAgain()
    {
        DictationStateMachine machine = Recording();
        machine.RequestCancel();
        machine.Reset();
        machine.EndTransition();

        Assert.Equal(DictationAction.Start, machine.RequestToggle());
    }

    // IsRecording is what the keyboard hook reads on every Escape press, so it has to be false
    // for anything other than a settled recording, including mid-start and mid-cancel.
    [Fact]
    public void IsRecording_IsTrueOnlyForASettledRecording()
    {
        var machine = new DictationStateMachine();
        Assert.False(machine.IsRecording);

        machine.RequestToggle();
        Assert.False(machine.IsRecording);

        machine.SetRecording();
        Assert.False(machine.IsRecording);

        machine.EndTransition();
        Assert.True(machine.IsRecording);

        machine.RequestCancel();
        Assert.False(machine.IsRecording);
    }

    [Fact]
    public void SetError_ExposesTheMessage()
    {
        var machine = new DictationStateMachine();
        machine.SetError("No speech detected");

        Assert.Equal(DictationState.Error, machine.State);
        Assert.Equal("No speech detected", machine.StatusMessage);
    }

    [Fact]
    public void Reset_ClearsAStaleErrorMessage()
    {
        var machine = new DictationStateMachine();
        machine.SetError("No speech detected");
        machine.Reset();

        Assert.Equal(DictationState.Idle, machine.State);
        Assert.Null(machine.StatusMessage);
    }

    /// <summary>A machine mid-recording with the gate released, the state a user is in while
    /// actually speaking.</summary>
    private static DictationStateMachine Recording()
    {
        var machine = new DictationStateMachine();
        machine.RequestToggle();
        machine.SetRecording();
        machine.EndTransition();
        return machine;
    }
}
