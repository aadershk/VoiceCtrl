using VoiceCtrl.Core.Dictation;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>Esc, then a new Dictation at once (VED-49 code review): the old recording is still being
/// stopped, so the new start has to wait for it rather than be swallowed by it.</summary>
public class CancelRaceTests
{
    [Fact]
    public async Task AHoldRightAfterEsc_StartsOnlyOnceTheCancelledRecordingHasStopped()
    {
        var rig = new Rig();
        await rig.Controller.ToggleBar();
        await rig.Controller.Tap();
        Assert.Equal(OverlayPhase.Listening, rig.Phase);

        rig.Recorder.StopGate = new TaskCompletionSource();
        rig.Controller.Cancel();
        Task hold = rig.Controller.HoldStart();

        Assert.False(hold.IsCompleted);
        Assert.Equal(1, rig.Recorder.Starts);

        rig.Recorder.StopGate.SetResult();
        await hold;

        Assert.Equal(2, rig.Recorder.Starts);
        Assert.True(rig.Recorder.IsRecording);
        Assert.Equal(OverlayPhase.Listening, rig.Phase);

        rig.Recorder.StopGate = null;
        rig.Transcriber.Returns("second try");
        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();
        Assert.Equal(["second try"], rig.Injector.Pasted);
    }

    [Fact]
    public async Task AStartWithNoCancelPending_IsImmediate()
    {
        var rig = new Rig();
        await rig.Controller.ToggleBar();

        Task tap = rig.Controller.Tap();

        Assert.True(tap.IsCompleted);
        Assert.Equal(OverlayPhase.Listening, rig.Phase);
    }
}
