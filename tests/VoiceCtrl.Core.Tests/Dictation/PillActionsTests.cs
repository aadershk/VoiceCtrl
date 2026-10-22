using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.Injection;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>The Overlay's actions and notices (VED-75): Finish while Listening, a press while
/// Transcribing, "Select a textbox first", the Paste Again key in "Paste failed", the three
/// "nothing heard" notices, a mic that stops mid-dictation, and "Taking longer than usual".</summary>
public class PillActionsTests
{
    private static async Task<Rig> ListeningRigAsync()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();
        return rig;
    }

    /// <summary>A Dictation stopped and waiting on a transcription the test completes (or cancels).
    /// <see cref="Stopping"/> finishes when the stop's work has.</summary>
    private sealed record Busy(Rig Rig, Task Stopping);

    private static async Task<Busy> TranscribingAsync()
    {
        var rig = new Rig();
        rig.Transcriber.Hold = true;
        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(600); Task stopping = rig.Controller.HoldEnd();
        Assert.Equal(OverlayPhase.Transcribing, rig.Phase);
        return new Busy(rig, stopping);
    }

    // Finish ---------------------------------------------------------------------------------

    [Fact]
    public async Task Listening_OffersFinishBesideCancel()
    {
        var rig = await ListeningRigAsync();

        Assert.Equal(OverlayControls.Timer | OverlayControls.Cancel | OverlayControls.Finish, rig.Controller.View.Controls);
    }

    [Fact]
    public async Task Finish_StopsAndPastes_LikeReleasingTheKey()
    {
        var rig = await ListeningRigAsync();

        await rig.Controller.Finish();

        Assert.Equal(["hello world"], rig.Injector.Pasted);
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);
        Assert.Contains(SoundCue.Stop, rig.Cues.Played);
    }

    [Fact]
    public async Task Finish_OnAHandsFreeDictation_Works()
    {
        var rig = new Rig();
        await rig.Controller.ToggleBar();
        await rig.Controller.Tap();

        await rig.Controller.Finish();

        Assert.Single(rig.Injector.Pasted);
    }

    [Fact]
    public async Task Finish_WhenNotListening_DoesNothing()
    {
        var rig = new Rig();

        await rig.Controller.Finish();

        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
        Assert.Empty(rig.Injector.Pasted);
    }

    [Fact]
    public async Task Finish_ThenReleasingTheHeldKey_DoesNotPasteTwice()
    {
        var rig = await ListeningRigAsync();

        await rig.Controller.Finish();
        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Single(rig.Injector.Pasted);
    }

    // A press while Transcribing ---------------------------------------------------------------

    [Fact]
    public async Task APressWhileTranscribing_ShowsAStillTranscribingNotice_WithCancel_AndKeepsTranscribing()
    {
        var (rig, stopping) = await TranscribingAsync();

        await rig.Controller.Tap();

        Assert.Equal(OverlayPhase.Notice, rig.Phase);
        Assert.Equal(OverlayNotice.StillTranscribing, rig.Controller.View.Notice);
        Assert.True(rig.Controller.View.Controls.HasFlag(OverlayControls.Cancel));
        Assert.True(rig.Controller.ShouldSwallowEscape);
        Assert.False(rig.Transcriber.LastToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData("hold")]
    [InlineData("tap")]
    [InlineData("pill")]
    [InlineData("rewrite")]
    public async Task EveryKindOfPress_ShowsTheNotice_AndNeverStartsANewDictation(string press)
    {
        var (rig, stopping) = await TranscribingAsync();

        await (press switch
        {
            "hold" => rig.Controller.HoldStart(),
            "tap" => rig.Controller.Tap(),
            "pill" => rig.Controller.PillPressed(),
            _ => rig.Controller.RewriteHoldStart(),
        });

        Assert.Equal(OverlayNotice.StillTranscribing, rig.Controller.View.Notice);
        Assert.Equal(1, rig.Recorder.Starts);
    }

    [Fact]
    public async Task ASecondPressWhileTheNoticeShows_CancelsTheTranscription()
    {
        var (rig, stopping) = await TranscribingAsync();
        await rig.Controller.Tap();

        await rig.Controller.Tap();

        Assert.True(rig.Transcriber.LastToken.IsCancellationRequested);
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
        Assert.True(rig.Controller.View.AfterCancel);
        Assert.Empty(rig.Injector.Pasted);
        Assert.Empty(rig.Controller.FailedDictations);
        Assert.False(rig.Controller.ShouldSwallowEscape);
    }

    [Fact]
    public async Task TheNoticesCancelButton_CancelsLikeEsc()
    {
        var (rig, stopping) = await TranscribingAsync();
        await rig.Controller.Tap();

        rig.Controller.Cancel();

        Assert.True(rig.Transcriber.LastToken.IsCancellationRequested);
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
    }

    [Fact]
    public async Task TheStillTranscribingNotice_GoesBackToTranscribing_AfterANoticeDuration()
    {
        var (rig, stopping) = await TranscribingAsync();
        await rig.Controller.Tap();

        rig.Timer.Advance(DictationController.NoticeDuration);

        Assert.Equal(OverlayPhase.Transcribing, rig.Phase);
        Assert.Equal(OverlayControls.Cancel, rig.Controller.View.Controls);
        rig.Transcriber.Complete("done");
        await stopping;
        Assert.Equal(["done"], rig.Injector.Pasted);
    }

    [Fact]
    public async Task ATranscriptionThatFinishesUnderTheNotice_Pastes_AsUsual()
    {
        var (rig, stopping) = await TranscribingAsync();
        await rig.Controller.Tap();

        rig.Transcriber.Complete("done");
        await stopping;

        Assert.Equal(["done"], rig.Injector.Pasted);
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);
    }

    [Fact]
    public async Task APressWhileIdle_ThroughPillPressed_DoesNothing()
    {
        var rig = new Rig();

        await rig.Controller.PillPressed();

        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
    }

    // Taking longer than usual -------------------------------------------------------------------

    [Fact]
    public async Task AfterTwelveSeconds_TranscribingBecomesATakingLongerNotice_WithCancel()
    {
        var (rig, stopping) = await TranscribingAsync();

        rig.Timer.Advance(DictationController.SlowThreshold - TimeSpan.FromMilliseconds(1));
        Assert.Equal(OverlayPhase.Transcribing, rig.Phase);
        rig.Timer.AdvanceMs(1);

        Assert.Equal(TimeSpan.FromSeconds(12), DictationController.SlowThreshold);
        Assert.Equal(OverlayNotice.TakingLonger, rig.Controller.View.Notice);
        Assert.True(rig.Controller.View.Controls.HasFlag(OverlayControls.Cancel));
        Assert.True(rig.Controller.ShouldSwallowEscape);
    }

    [Fact]
    public async Task TheTakingLongerNotice_StaysUntilTheTranscriptionEnds()
    {
        var (rig, stopping) = await TranscribingAsync();
        rig.Timer.Advance(DictationController.SlowThreshold);

        rig.Timer.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(OverlayNotice.TakingLonger, rig.Controller.View.Notice);

        rig.Transcriber.Complete("finally");
        await stopping;
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);
        Assert.Equal(["finally"], rig.Injector.Pasted);
    }

    [Fact]
    public async Task APressOnTheTakingLongerNotice_Cancels()
    {
        var (rig, stopping) = await TranscribingAsync();
        rig.Timer.Advance(DictationController.SlowThreshold);

        await rig.Controller.HoldStart();

        Assert.True(rig.Transcriber.LastToken.IsCancellationRequested);
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
    }

    [Fact]
    public async Task AFastTranscription_NeverShowsTakingLonger()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        rig.Timer.Advance(TimeSpan.FromMinutes(1));

        Assert.DoesNotContain(rig.Views, v => v.Notice == OverlayNotice.TakingLonger);
    }

    [Fact]
    public async Task ACancelledSlowTranscription_NeverComesBackAsANotice()
    {
        var (rig, stopping) = await TranscribingAsync();
        rig.Controller.Cancel();

        rig.Timer.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
    }

    [Fact]
    public async Task TheStillTranscribingNotice_ReturnsToTakingLonger_WhenItIsPastTheThreshold()
    {
        var (rig, stopping) = await TranscribingAsync();
        rig.Timer.Advance(DictationController.SlowThreshold - TimeSpan.FromSeconds(1));
        await rig.Controller.Tap();

        rig.Timer.Advance(DictationController.NoticeDuration);

        Assert.Equal(OverlayNotice.TakingLonger, rig.Controller.View.Notice);
    }

    // No text box / Paste failed ------------------------------------------------------------------

    [Fact]
    public async Task APasteWithNoTextBoxFocused_SaysSoInsteadOfPasted_AndKeepsTheText()
    {
        var rig = new Rig();
        rig.Injector.Result = InjectionResult.ClipboardOnlyNoTextBox;
        await rig.Controller.HoldStart();

        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Equal(OverlayNotice.SelectTextboxFirst, rig.Controller.View.Notice);
        Assert.DoesNotContain(SoundCue.Done, rig.Cues.Played);
        Assert.Equal("hello world", rig.Last.Text);
        Assert.Single(rig.History.Entries);
    }

    [Fact]
    public async Task APasteWithNoTextBox_DoesNotSendAPendingEnter()
    {
        var rig = new Rig { PressEnter = true };
        rig.Transcriber.Returns("hello press enter");
        rig.Injector.Result = InjectionResult.ClipboardOnlyNoTextBox;
        await rig.Controller.HoldStart();

        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Equal(0, rig.Injector.Enters);
    }

    [Fact]
    public async Task PasteAgainWithNoTextBox_ShowsTheSameNotice()
    {
        var rig = new Rig();
        rig.Last.Set("kept");
        rig.Injector.Result = InjectionResult.ClipboardOnlyNoTextBox;

        InjectionResult? result = await rig.Controller.PasteAgain();

        Assert.Equal(InjectionResult.ClipboardOnlyNoTextBox, result);
        Assert.Equal(OverlayNotice.SelectTextboxFirst, rig.Controller.View.Notice);
    }

    [Fact]
    public async Task PasteFailed_NamesThePasteAgainKey_FromSettings()
    {
        var rig = new Rig { PasteAgainKey = "Ctrl+Alt+P" };
        rig.Injector.Result = InjectionResult.Failed;
        await rig.Controller.HoldStart();

        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Equal(OverlayNotice.PasteFailed, rig.Controller.View.Notice);
        Assert.Equal("Ctrl+Alt+P", rig.Controller.View.NoticeKey);
    }

    [Fact]
    public async Task PasteFailed_WithoutAPasteAgainKey_CarriesNone()
    {
        var rig = new Rig { PasteAgainKey = null };
        rig.Injector.Result = InjectionResult.Failed;
        await rig.Controller.HoldStart();

        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Null(rig.Controller.View.NoticeKey);
    }

    [Fact]
    public async Task PasteFailedFromPasteAgain_NamesTheKeyToo()
    {
        var rig = new Rig { PasteAgainKey = "Ctrl+Shift+V" };
        rig.Last.Set("kept");
        rig.Injector.Result = InjectionResult.Failed;

        await rig.Controller.PasteAgain();

        Assert.Equal("Ctrl+Shift+V", rig.Controller.View.NoticeKey);
    }

    // Nothing heard --------------------------------------------------------------------------------

    [Fact]
    public async Task AClipWithNoSignalAtAll_SaysTheMicrophoneGaveNoAudio_WithoutCallingTheService()
    {
        var rig = new Rig();
        rig.Recorder.ClipBytes = WavBytes.Silent(TimeSpan.FromSeconds(1));
        await rig.Controller.HoldStart();

        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Equal(OverlayNotice.NoMicAudio, rig.Controller.View.Notice);
        Assert.Empty(rig.Transcriber.Calls);
        Assert.Empty(rig.Controller.FailedDictations);
        Assert.False(rig.Controller.ShouldSwallowEscape);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task AServiceThatHearsNoWords_SaysSo_AndPastesNothing(string? heard)
    {
        var rig = new Rig();
        rig.Transcriber.Returns(heard);
        await rig.Controller.HoldStart();

        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Equal(OverlayPhase.Notice, rig.Phase);
        Assert.Equal(OverlayNotice.NoWordsHeard, rig.Controller.View.Notice);
        Assert.Empty(rig.Injector.Pasted);
        Assert.Empty(rig.History.Entries);
    }

    [Fact]
    public async Task ATooShortClip_StillShowsTheNoSpeechPill()
    {
        var rig = new Rig();
        rig.Recorder.ClipLength = TimeSpan.FromMilliseconds(100);
        await rig.Controller.HoldStart();

        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Equal(OverlayPhase.NoSpeech, rig.Phase);
    }

    // The microphone stops mid-dictation ------------------------------------------------------------

    [Fact]
    public async Task AMicrophoneThatStopsWhileListening_ShowsMicStopped_AndThrowsTheRecordingAway()
    {
        var rig = await ListeningRigAsync();

        rig.Controller.MicrophoneStopped();

        Assert.Equal(OverlayNotice.MicStopped, rig.Controller.View.Notice);
        Assert.False(rig.Ducker.IsDucked);
        Assert.False(rig.Controller.ShouldSwallowEscape);
        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();
        Assert.Empty(rig.Transcriber.Calls);
        Assert.Empty(rig.Injector.Pasted);
    }

    [Fact]
    public void AMicrophoneThatStops_WhileNothingIsListening_ChangesNothing()
    {
        var rig = new Rig();

        rig.Controller.MicrophoneStopped();

        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
    }

    [Fact]
    public async Task AMicrophoneThatStops_ThenANewDictation_Works()
    {
        var rig = await ListeningRigAsync();
        rig.Controller.MicrophoneStopped();
        rig.Timer.Advance(DictationController.NoticeDuration);

        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Single(rig.Injector.Pasted);
    }
}
