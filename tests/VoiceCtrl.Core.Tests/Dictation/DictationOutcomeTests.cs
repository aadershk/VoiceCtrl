using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.Injection;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>No speech, the short Overlay messages, Sound Cues and Mute Other Audio (spec §5.1;
/// research 11, States and Sound Cues).</summary>
public class DictationOutcomeTests
{
    [Fact]
    public async Task ATooShortClip_ShowsNoSpeechFor1800ms_WithoutCallingTheService()
    {
        var rig = new Rig();
        rig.Recorder.ClipLength = TimeSpan.FromMilliseconds(100);
        await rig.Controller.ToggleBar();
        await rig.Controller.Tap();

        await rig.Controller.Tap();

        Assert.Empty(rig.Transcriber.Calls);
        Assert.Equal(OverlayPhase.NoSpeech, rig.Phase);
        rig.Timer.AdvanceMs(1799);
        Assert.Equal(OverlayPhase.NoSpeech, rig.Phase);
        rig.Timer.AdvanceMs(1);
        Assert.Equal(OverlayPhase.Ready, rig.Phase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task TheServiceHearingNothing_ShowsNoWordsHeard_AndPastesNothing(string? heard)
    {
        var rig = new Rig();
        rig.Transcriber.Returns(heard);
        await rig.Controller.HoldStart();

        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Equal(OverlayNotice.NoWordsHeard, rig.Controller.View.Notice);
        Assert.Empty(rig.Injector.Pasted);
        Assert.Empty(rig.History.Entries);
        Assert.Empty(rig.Controller.FailedDictations);
    }

    [Fact]
    public async Task AMicrophoneThatWontOpen_ShowsMicUnavailable_ThenReturnsToRest()
    {
        var rig = new Rig();
        rig.Recorder.StartThrows = new InvalidOperationException("no device");

        await rig.Controller.HoldStart();

        Assert.Equal(OverlayNotice.MicUnavailable, rig.Controller.View.Notice);
        Assert.False(rig.Ducker.IsDucked);
        Assert.Empty(rig.Cues.Played);
        rig.Timer.Advance(DictationController.NoticeDuration);
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
    }

    [Fact]
    public async Task AMicrophoneThatFailsAtStop_ShowsMicUnavailable_AndKeepsNothing()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();
        rig.Recorder.StopThrows = new InvalidOperationException("device unplugged");

        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Equal(OverlayNotice.MicUnavailable, rig.Controller.View.Notice);
        Assert.Empty(rig.Transcriber.Calls);
        Assert.Empty(rig.Controller.FailedDictations);
        Assert.False(rig.Controller.ShouldSwallowEscape);
    }

    [Fact]
    public async Task AMissingApiKey_ShowsAddAnApiKey_InsteadOfTranscribing()
    {
        var rig = new Rig { ApiKeyMissing = true };
        await rig.Controller.HoldStart();

        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Empty(rig.Transcriber.Calls);
        Assert.Equal(OverlayNotice.AddApiKey, rig.Controller.View.Notice);
    }

    [Theory]
    [InlineData(InjectionResult.ClipboardOnlyElevatedTarget, OverlayNotice.CopiedPressCtrlV)]
    [InlineData(InjectionResult.Failed, OverlayNotice.PasteFailed)]
    public async Task APasteThatDidNotLand_SaysSo_AndTheTextIsStillKept(InjectionResult result, OverlayNotice expected)
    {
        var rig = new Rig();
        rig.Injector.Result = result;
        rig.Transcriber.Returns("keep me");
        await rig.Controller.HoldStart();

        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Equal(OverlayPhase.Notice, rig.Phase);
        Assert.Equal(expected, rig.Controller.View.Notice);
        Assert.Equal("keep me", rig.Last.Text);
        Assert.Equal("keep me", rig.History.Entries[0].Text);
    }

    [Fact]
    public async Task SoundCues_MarkStartStopAndDone()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();
        Assert.Equal([SoundCue.Start], rig.Cues.Played);

        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Equal([SoundCue.Start, SoundCue.Stop, SoundCue.Done], rig.Cues.Played);
    }

    [Fact]
    public async Task SoundCues_HaveNoCueForCancelOrNoSpeech()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();
        rig.Controller.Cancel();
        Assert.Equal([SoundCue.Start], rig.Cues.Played);

        rig.Transcriber.Returns(null);
        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();
        Assert.Equal([SoundCue.Start, SoundCue.Start, SoundCue.Stop], rig.Cues.Played);
    }

    [Fact]
    public async Task OtherAudio_IsLoweredOnlyWhileListening()
    {
        var rig = new Rig();
        rig.Transcriber.Hold = true;

        await rig.Controller.HoldStart();
        Assert.True(rig.Ducker.IsDucked);

        rig.Timer.AdvanceMs(600); Task stopping = rig.Controller.HoldEnd();
        Assert.False(rig.Ducker.IsDucked);

        rig.Transcriber.Complete("x");
        await stopping;
    }

    [Fact]
    public async Task OtherAudio_IsRestoredWhenADictationIsCancelled()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();

        rig.Controller.Cancel();

        Assert.False(rig.Ducker.IsDucked);
    }
}
