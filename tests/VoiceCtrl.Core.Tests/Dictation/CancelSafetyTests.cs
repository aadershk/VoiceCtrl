using VoiceCtrl.Core.Dictation;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>Cancel safety (VED-76, ADR-0004): a cancelled recording of more than 3 s is kept in
/// memory with an Undo notice, and recovered on demand.</summary>
public class CancelSafetyTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(5);

    /// <summary>Starts a Dictation from the Ready tile and speaks for <paramref name="length"/>.</summary>
    private static async Task<Rig> ListeningAsync(TimeSpan length, Action<Rig>? arrange = null)
    {
        var rig = new Rig();
        arrange?.Invoke(rig);
        rig.Recorder.ClipLength = length;
        await rig.Controller.ToggleBar();
        await rig.Controller.Tap();
        rig.Timer.Advance(length);
        return rig;
    }

    [Fact]
    public async Task CancellingAfterMoreThan3s_KeepsTheRecording_AndOffersUndo()
    {
        var rig = await ListeningAsync(Long);

        rig.Controller.Cancel();

        FailedDictation kept = Assert.Single(rig.Controller.FailedDictations);
        Assert.Equal(FailureReason.Cancelled, kept.Reason);
        Assert.Equal("Notepad", kept.AppName);
        Assert.Equal(OverlayPhase.Notice, rig.Phase);
        Assert.Equal(OverlayNotice.CancelledUndo, rig.Controller.View.Notice);
        Assert.Equal(OverlayControls.Undo, rig.Controller.View.Controls);
        Assert.Equal(kept.Id, rig.Controller.View.FailedId);
        Assert.Empty(rig.Transcriber.Calls);
        Assert.Empty(rig.Injector.Pasted);
        Assert.False(rig.Controller.ShouldSwallowEscape);
    }

    [Theory]
    [InlineData(3000)]
    [InlineData(1200)]
    public async Task CancellingAt3sOrLess_JustDropsIt(int milliseconds)
    {
        var rig = await ListeningAsync(TimeSpan.FromMilliseconds(milliseconds));

        rig.Controller.Cancel();

        Assert.Empty(rig.Controller.FailedDictations);
        Assert.Equal(OverlayPhase.Ready, rig.Phase);
    }

    [Fact]
    public async Task WithKeepDictationsOff_NothingIsKept()
    {
        var rig = await ListeningAsync(Long, r => r.KeepCancelled = false);

        rig.Controller.Cancel();

        Assert.Empty(rig.Controller.FailedDictations);
        Assert.Equal(OverlayPhase.Ready, rig.Phase);
    }

    [Fact]
    public async Task ACancelledRewrite_IsNotKept()
    {
        var rig = new Rig();
        rig.Selection.Selected = "Some text.";
        rig.Recorder.ClipLength = Long;
        await rig.Controller.RewriteHoldStart();
        rig.Timer.Advance(Long);

        rig.Controller.Cancel();

        Assert.Empty(rig.Controller.FailedDictations);
    }

    [Fact]
    public async Task ARecordingWithNoSoundInIt_IsNotKept()
    {
        var rig = await ListeningAsync(Long, r => r.Recorder.ClipBytes = WavBytes.Silent(Long));

        rig.Controller.Cancel();

        Assert.Empty(rig.Controller.FailedDictations);
    }

    [Fact]
    public async Task Undo_TranscribesTheKeptRecording_AndPastesIt()
    {
        var rig = await ListeningAsync(Long);
        rig.Controller.Cancel();
        Guid id = rig.Controller.FailedDictations[0].Id;
        rig.Transcriber.Returns("saved by undo");

        await rig.Controller.Undo(id);

        Assert.Equal(["saved by undo"], rig.Injector.Pasted);
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);
        Assert.Empty(rig.Controller.FailedDictations);
        Assert.Equal("saved by undo", rig.History.Entries[0].Text);
    }

    [Fact]
    public async Task Undo_OnlyActsOnTheNoticeTheOverlayIsShowing()
    {
        var rig = await ListeningAsync(Long);
        rig.Controller.Cancel();
        rig.Timer.Advance(DictationController.FailedDuration);

        await rig.Controller.Undo(rig.Controller.FailedDictations[0].Id);

        Assert.Empty(rig.Transcriber.Calls);
        Assert.Single(rig.Controller.FailedDictations);
    }

    [Fact]
    public async Task TheUndoNotice_LeavesAfter8s_ButTheRecordingStaysForRecovery()
    {
        var rig = await ListeningAsync(Long);
        rig.Controller.Cancel();

        rig.Timer.Advance(DictationController.FailedDuration);

        Assert.Equal(OverlayPhase.Ready, rig.Phase);
        Assert.Single(rig.Controller.FailedDictations);
    }

    [Fact]
    public async Task ARecoveryFromTheDictationsScreen_TranscribesIt_WithoutPasting()
    {
        var rig = await ListeningAsync(Long);
        rig.Controller.Cancel();
        Guid id = rig.Controller.FailedDictations[0].Id;
        rig.Transcriber.Returns("recovered later");

        RetryOutcome outcome = await rig.Controller.RetryFromDictations(id, offline: false);

        Assert.Equal(RetryOutcome.Transcribed, outcome);
        Assert.Empty(rig.Injector.Pasted);
        Assert.Equal("recovered later", rig.History.Entries[0].Text);
        Assert.Empty(rig.Controller.FailedDictations);
    }

    [Fact]
    public async Task TheAudioIsDroppedAfterTheRecoveryWindow()
    {
        var rig = await ListeningAsync(Long);
        rig.Controller.Cancel();

        rig.Timer.Advance(DictationController.CancelledRecoveryWindow - TimeSpan.FromSeconds(1));
        Assert.Single(rig.Controller.FailedDictations);
        rig.Timer.Advance(TimeSpan.FromSeconds(1));

        Assert.Empty(rig.Controller.FailedDictations);
    }

    [Fact]
    public async Task ARecoveryThatFails_BecomesAnOrdinaryFailedDictation_ThatIsNotDropped()
    {
        var rig = await ListeningAsync(Long);
        rig.Controller.Cancel();
        Guid id = rig.Controller.FailedDictations[0].Id;
        rig.Transcriber.Throws(Rig.NoInternet());

        RetryOutcome outcome = await rig.Controller.RetryFromDictations(id, offline: false);
        rig.Timer.Advance(DictationController.CancelledRecoveryWindow + TimeSpan.FromMinutes(1));

        Assert.Equal(RetryOutcome.Failed, outcome);
        FailedDictation kept = Assert.Single(rig.Controller.FailedDictations);
        Assert.Equal(FailureReason.NoInternet, kept.Reason);
    }

    [Fact]
    public async Task CancellingWhileTranscribing_KeepsTheClipToo()
    {
        var rig = await ListeningAsync(Long, r => r.Transcriber.Hold = true);
        Task stop = rig.Controller.Tap();
        Assert.Equal(OverlayPhase.Transcribing, rig.Phase);

        rig.Controller.Cancel();
        await stop;

        FailedDictation kept = Assert.Single(rig.Controller.FailedDictations);
        Assert.Equal(FailureReason.Cancelled, kept.Reason);
        Assert.Equal(OverlayNotice.CancelledUndo, rig.Controller.View.Notice);
        Assert.Empty(rig.Injector.Pasted);

        rig.Transcriber.Hold = false;
        rig.Transcriber.Returns("after all");
        await rig.Controller.Undo(kept.Id);
        Assert.Equal(["after all"], rig.Injector.Pasted);
    }

    [Fact]
    public async Task TurningKeepDictationsOff_DropsWhatIsHeld()
    {
        var rig = await ListeningAsync(Long);
        rig.Controller.Cancel();

        rig.Controller.DropCancelledRecordings();

        Assert.Empty(rig.Controller.FailedDictations);
    }

    [Fact]
    public async Task AnUndoPressedWhileTheRecorderIsStillStopping_WaitsForTheKeptClip()
    {
        var rig = await ListeningAsync(Long);
        rig.Recorder.StopGate = new TaskCompletionSource();
        rig.Controller.Cancel();
        Guid noticeId = rig.Controller.View.FailedId!.Value;
        rig.Transcriber.Returns("patient");

        Task undo = rig.Controller.Undo(noticeId);
        Assert.False(undo.IsCompleted);
        rig.Recorder.StopGate.SetResult();
        await undo;

        Assert.Equal(["patient"], rig.Injector.Pasted);
    }
}
