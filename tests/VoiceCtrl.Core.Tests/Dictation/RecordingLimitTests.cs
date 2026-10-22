using VoiceCtrl.Core.Dictation;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>The 20-minute recording cap (VED-76): a warning a minute before, then the recording
/// ends as if the user had finished it, and its text is not lost.</summary>
public class RecordingLimitTests
{
    private static async Task<Rig> ListeningAsync()
    {
        var rig = new Rig();
        await rig.Controller.ToggleBar();
        await rig.Controller.Tap();
        return rig;
    }

    private static readonly TimeSpan Warning = DictationController.RecordingLimit - DictationController.RecordingLimitWarning;

    [Fact]
    public async Task AMinuteBeforeTheCap_ThePillWarns_AndThenGoesBackToListening()
    {
        var rig = await ListeningAsync();

        rig.Timer.Advance(Warning);
        Assert.Equal(OverlayPhase.Notice, rig.Phase);
        Assert.Equal(OverlayNotice.RecordingLimitSoon, rig.Controller.View.Notice);
        Assert.True(rig.Controller.ShouldSwallowEscape);

        rig.Timer.Advance(DictationController.NoticeDuration);
        Assert.Equal(OverlayPhase.Listening, rig.Phase);
        Assert.Equal(1, rig.Recorder.Starts);
    }

    [Fact]
    public async Task AtTheCap_TheRecordingEndsAndItsTextIsPasted()
    {
        var rig = await ListeningAsync();
        rig.Transcriber.Returns("a very long dictation");

        rig.Timer.Advance(DictationController.RecordingLimit);

        Assert.Equal(["a very long dictation"], rig.Injector.Pasted);
        Assert.Equal(OverlayPhase.Notice, rig.Phase);
        Assert.Equal(OverlayNotice.RecordingLimitReached, rig.Controller.View.Notice);
        Assert.False(rig.Recorder.IsRecording);
    }

    [Fact]
    public async Task AtTheCap_ATranscriptionThatFails_IsKeptForRetry_NotLost()
    {
        var rig = await ListeningAsync();
        rig.Transcriber.Throws(Rig.TimedOut());

        rig.Timer.Advance(DictationController.RecordingLimit);

        Assert.Equal(OverlayPhase.Failed, rig.Phase);
        Assert.Single(rig.Controller.FailedDictations);
    }

    [Fact]
    public async Task FinishingDuringTheWarning_StopsAsUsual_AndTheCapNeverFiresAfterwards()
    {
        var rig = await ListeningAsync();
        rig.Timer.Advance(Warning);

        await rig.Controller.Finish();
        Assert.Equal(["hello world"], rig.Injector.Pasted);
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);

        rig.Timer.Advance(DictationController.RecordingLimit);
        Assert.Single(rig.Injector.Pasted);
    }

    [Fact]
    public async Task ATapDuringTheWarning_StopsTheRecording_InsteadOfStartingAnother()
    {
        var rig = await ListeningAsync();
        rig.Timer.Advance(Warning);

        await rig.Controller.Tap();

        Assert.Equal(["hello world"], rig.Injector.Pasted);
        Assert.Equal(1, rig.Recorder.Starts);
    }

    [Fact]
    public async Task ACancelBeforeTheCap_NeverHitsIt()
    {
        var rig = await ListeningAsync();
        rig.Controller.Cancel();

        rig.Timer.Advance(DictationController.RecordingLimit);

        Assert.Empty(rig.Injector.Pasted);
        Assert.Empty(rig.Transcriber.Calls);
    }
}
