using VoiceCtrl.Core.Dictation;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>Hold-to-Talk like Wispr (VED-74): record from the key press, a tap does nothing, and
/// two quick presses lock the recording on hands-free.</summary>
public class HoldToTalkTests
{
    [Fact]
    public async Task RecordingStartsOnThePress_NotAfterAHoldDelay()
    {
        var rig = new Rig();

        await rig.Controller.HoldStart();

        Assert.True(rig.Recorder.IsRecording);
        Assert.Equal(OverlayPhase.Listening, rig.Phase);
    }

    [Fact]
    public async Task ATapUnder500ms_DoesNothing_NoStopCue_NoTranscribing_NoNotice_NothingTranscribed()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(120);

        await rig.Controller.HoldEnd();

        Assert.False(rig.Recorder.IsRecording);
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
        Assert.DoesNotContain(rig.Cues.Played, cue => cue == SoundCue.Stop);
        Assert.DoesNotContain(rig.Views, view => view.Phase is OverlayPhase.Transcribing or OverlayPhase.NoSpeech or OverlayPhase.Notice);
        Assert.Empty(rig.Transcriber.Calls);
        Assert.Empty(rig.Injector.Pasted);
        Assert.DoesNotContain(rig.Views, view => view.AfterCancel);
    }

    [Fact]
    public async Task ATap_WhileTheReadyTileShows_ReturnsToTheReadyTile()
    {
        var rig = new Rig();
        await rig.Controller.ToggleBar();

        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(80);
        await rig.Controller.HoldEnd();

        Assert.Equal(OverlayPhase.Ready, rig.Phase);
    }

    [Fact]
    public async Task AHoldOf500ms_IsARealHold_ItTranscribesAndPastes()
    {
        var rig = new Rig();
        rig.Transcriber.Returns("hello there");
        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(500);

        await rig.Controller.HoldEnd();

        Assert.Equal(["hello there"], rig.Injector.Pasted);
    }

    [Fact]
    public async Task TwoQuickPresses_LockTheRecordingOn_ReleasingDoesNotStopIt()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(100);
        await rig.Controller.HoldEnd();
        rig.Timer.AdvanceMs(150);

        await rig.Controller.HoldStart();

        Assert.Equal(OverlayPhase.Listening, rig.Phase);
        Assert.True(rig.Controller.View.Locked);

        rig.Timer.AdvanceMs(2000);
        await rig.Controller.HoldEnd();

        Assert.Equal(OverlayPhase.Listening, rig.Phase);
        Assert.True(rig.Recorder.IsRecording);
        Assert.Empty(rig.Injector.Pasted);
    }

    [Fact]
    public async Task ALockedRecording_EndsOnTheNextPress_AndPastes()
    {
        var rig = new Rig();
        rig.Transcriber.Returns("locked words");
        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(100);
        await rig.Controller.HoldEnd();
        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(100);
        await rig.Controller.HoldEnd();
        rig.Timer.AdvanceMs(8000);

        await rig.Controller.HoldStart();
        await rig.Controller.HoldEnd();

        Assert.Equal(["locked words"], rig.Injector.Pasted);
        Assert.False(rig.Recorder.IsRecording);
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);
    }

    [Fact]
    public async Task ASecondPressMoreThan500msAfterTheFirstStarted_DoesNotLock()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(100);
        await rig.Controller.HoldEnd();
        rig.Timer.AdvanceMs(450);

        await rig.Controller.HoldStart();

        Assert.False(rig.Controller.View.Locked);
        rig.Timer.AdvanceMs(600);
        await rig.Controller.HoldEnd();
        Assert.False(rig.Recorder.IsRecording);
    }

    [Fact]
    public async Task ALockedRecording_CanBeCancelledWithEsc_AndFinishedFromThePill()
    {
        var rig = new Rig();
        await StartLocked(rig);

        rig.Controller.Cancel();
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
        Assert.False(rig.Recorder.IsRecording);

        await StartLocked(rig);
        await rig.Controller.Finish();
        Assert.Single(rig.Injector.Pasted);
    }

    [Fact]
    public async Task APressAfterALockedRecordingEnded_StartsFresh_NotLocked()
    {
        var rig = new Rig();
        await StartLocked(rig);
        await rig.Controller.HoldStart();
        rig.Timer.Advance(DictationController.PastedDuration);

        await rig.Controller.HoldStart();

        Assert.False(rig.Controller.View.Locked);
    }

    [Fact]
    public async Task TheEscThenHoldRace_StillWaitsForTheCancelledRecording_ThenSurvivesATapAndALock()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(700);
        rig.Recorder.StopGate = new TaskCompletionSource();
        rig.Controller.Cancel();

        Task hold = rig.Controller.HoldStart();
        Assert.False(hold.IsCompleted);
        rig.Recorder.StopGate.SetResult();
        await hold;

        Assert.True(rig.Recorder.IsRecording);
        Assert.Equal(2, rig.Recorder.Starts);
    }

    [Fact]
    public async Task ARewriteTap_IsDroppedQuietly_AndNeverLocks()
    {
        var rig = new Rig();
        await rig.Controller.RewriteHoldStart();
        rig.Timer.AdvanceMs(300);

        await rig.Controller.RewriteHoldEnd();

        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
        Assert.DoesNotContain(rig.Views, view => view.Phase is OverlayPhase.Transcribing or OverlayPhase.NoSpeech or OverlayPhase.Notice);
        Assert.Empty(rig.Rewriter.Calls);

        await rig.Controller.RewriteHoldStart();
        Assert.False(rig.Controller.View.Locked);
    }

    [Fact]
    public async Task ARewriteHold_StillRewritesOnRelease()
    {
        var rig = new Rig();
        rig.Selection.Selected = "some text";
        await rig.Controller.RewriteHoldStart();
        rig.Timer.AdvanceMs(1500);

        await rig.Controller.RewriteHoldEnd();

        Assert.Single(rig.Rewriter.Calls);
        Assert.Equal(["REWRITTEN"], rig.Injector.Pasted);
    }

    private static async Task StartLocked(Rig rig)
    {
        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(100);
        await rig.Controller.HoldEnd();
        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(100);
        await rig.Controller.HoldEnd();
        Assert.True(rig.Controller.View.Locked);
    }
}
