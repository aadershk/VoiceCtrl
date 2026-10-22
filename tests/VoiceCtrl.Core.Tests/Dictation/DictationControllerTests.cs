using VoiceCtrl.Core.Dictation;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>Gestures, outcomes and "where it ends up" (spec §5.1; research 11, States).</summary>
public class DictationControllerTests
{
    [Fact]
    public async Task HotkeyDoubleTap_ShowsTheReadyTile_AndASecondOneHidesIt()
    {
        var rig = new Rig();

        await rig.Controller.ToggleBar();
        Assert.Equal(OverlayPhase.Ready, rig.Phase);
        Assert.Equal(OverlayControls.Start, rig.Controller.View.Controls);

        await rig.Controller.ToggleBar();
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
        Assert.False(rig.Recorder.IsRecording);
    }

    [Fact]
    public async Task TapOnTheReadyTile_Listens_AndASecondTapTranscribesAndPastes()
    {
        var rig = new Rig();
        await rig.Controller.ToggleBar();
        rig.Transcriber.Returns("Meet at noon.");

        await rig.Controller.Tap();
        Assert.Equal(OverlayPhase.Listening, rig.Phase);
        Assert.True(rig.Recorder.IsRecording);
        Assert.Equal(rig.Timer.UtcNow, rig.Controller.View.ListeningSince);
        Assert.Equal(OverlayControls.Timer | OverlayControls.Cancel | OverlayControls.Finish, rig.Controller.View.Controls);

        await rig.Controller.Tap();
        Assert.Contains(rig.Views, v => v.Phase == OverlayPhase.Transcribing && v.Controls == OverlayControls.Cancel);
        Assert.Equal(["Meet at noon."], rig.Injector.Pasted);
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);
        Assert.Equal("Meet at noon.", rig.Last.Text);
        Assert.Equal("Meet at noon.", rig.History.Entries[0].Text);
    }

    [Fact]
    public async Task Pasted_ShowsFor900ms_ThenReturnsToTheReadyTileAfterAHotkeyShow()
    {
        var rig = new Rig();
        await rig.Controller.ToggleBar();
        await rig.Controller.Tap();
        await rig.Controller.Tap();

        rig.Timer.AdvanceMs(899);
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);

        rig.Timer.AdvanceMs(1);
        Assert.Equal(OverlayPhase.Ready, rig.Phase);
    }

    [Fact]
    public async Task TriggerKeyHold_FromHidden_ListensAtOnce_AndReleasePastesThenHides()
    {
        var rig = new Rig();

        await rig.Controller.HoldStart();
        Assert.Equal(OverlayPhase.Listening, rig.Phase);

        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();
        Assert.Single(rig.Injector.Pasted);
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);

        rig.Timer.Advance(DictationController.PastedDuration);
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
    }

    [Fact]
    public async Task TriggerKeyHold_WhileTheReadyTileShows_EndsBackOnTheReadyTile()
    {
        var rig = new Rig();
        await rig.Controller.ToggleBar();

        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();
        rig.Timer.Advance(DictationController.PastedDuration);

        Assert.Equal(OverlayPhase.Ready, rig.Phase);
    }

    [Fact]
    public async Task HoldStart_WhileADictationIsListening_IsIgnored_AndItsReleaseDoesNotStopATapDictation()
    {
        var rig = new Rig();
        await rig.Controller.ToggleBar();
        await rig.Controller.Tap();

        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Equal(1, rig.Recorder.Starts);
        Assert.Equal(OverlayPhase.Listening, rig.Phase);
    }

    [Fact]
    public async Task HotkeyDoubleTap_WhileListening_StopsAndPastes_ThenEndsHidden()
    {
        var rig = new Rig();
        await rig.Controller.ToggleBar();
        await rig.Controller.Tap();

        await rig.Controller.ToggleBar();

        Assert.Single(rig.Injector.Pasted);
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);
        rig.Timer.Advance(DictationController.PastedDuration);
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
    }

    [Fact]
    public async Task HotkeyDoubleTap_WhileTranscribing_LetsItFinish_ThenEndsHidden()
    {
        var rig = new Rig();
        rig.Transcriber.Hold = true;
        await rig.Controller.ToggleBar();
        await rig.Controller.Tap();
        Task stopping = rig.Controller.Tap();

        await rig.Controller.ToggleBar();
        Assert.Equal(OverlayPhase.Transcribing, rig.Phase);

        rig.Transcriber.Complete("done");
        await stopping;
        Assert.Equal(["done"], rig.Injector.Pasted);
        rig.Timer.Advance(DictationController.PastedDuration);
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
    }

    [Fact]
    public async Task HotkeyDoubleTap_WhileAnOutcomeShows_HidesAtOnce()
    {
        var rig = new Rig();
        await rig.Controller.ToggleBar();
        await rig.Controller.Tap();
        await rig.Controller.Tap();

        await rig.Controller.ToggleBar();

        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
        rig.Timer.Advance(DictationController.PastedDuration);
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
    }

    [Fact]
    public async Task TapWhileAnOutcomeShows_StartsTheNextDictation()
    {
        var rig = new Rig();
        await rig.Controller.ToggleBar();
        await rig.Controller.Tap();
        await rig.Controller.Tap();

        await rig.Controller.Tap();
        Assert.Equal(OverlayPhase.Listening, rig.Phase);

        rig.Timer.Advance(DictationController.PastedDuration);
        Assert.Equal(OverlayPhase.Listening, rig.Phase);
    }

    [Fact]
    public async Task Escape_IsSwallowedOnlyWhileListeningOrTranscribing()
    {
        var rig = new Rig();
        rig.Transcriber.Hold = true;
        await rig.Controller.ToggleBar();
        Assert.False(rig.Controller.ShouldSwallowEscape);

        await rig.Controller.Tap();
        Assert.True(rig.Controller.ShouldSwallowEscape);

        Task stopping = rig.Controller.Tap();
        Assert.True(rig.Controller.ShouldSwallowEscape);

        rig.Transcriber.Complete("x");
        await stopping;
        Assert.False(rig.Controller.ShouldSwallowEscape);
    }

    [Fact]
    public async Task CancelWhileListening_DiscardsTheAudio_AndReturnsToRestAsACancel()
    {
        var rig = new Rig();
        await rig.Controller.ToggleBar();
        await rig.Controller.Tap();

        rig.Controller.Cancel();

        Assert.False(rig.Recorder.IsRecording);
        Assert.Empty(rig.Transcriber.Calls);
        Assert.Empty(rig.Injector.Pasted);
        Assert.Empty(rig.Controller.FailedDictations);
        Assert.Equal(OverlayPhase.Ready, rig.Phase);
        Assert.True(rig.Controller.View.AfterCancel);
        Assert.False(rig.Controller.ShouldSwallowEscape);
    }

    [Fact]
    public async Task CancelWhileTranscribing_AbortsTheRequest_PastesNothing_AndKeepsNothingForRetry()
    {
        var rig = new Rig();
        rig.Transcriber.Hold = true;
        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(600); Task stopping = rig.Controller.HoldEnd();

        rig.Controller.Cancel();
        await stopping;

        Assert.True(rig.Transcriber.LastToken.IsCancellationRequested);
        Assert.Empty(rig.Injector.Pasted);
        Assert.Empty(rig.Controller.FailedDictations);
        Assert.Null(rig.Last.Text);
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
    }

    [Fact]
    public async Task Cancel_WhenNothingIsInProgress_DoesNothing()
    {
        var rig = new Rig();
        await rig.Controller.ToggleBar();
        int viewsBefore = rig.Views.Count;

        rig.Controller.Cancel();

        Assert.Equal(viewsBefore, rig.Views.Count);
    }

    [Fact]
    public async Task TapWhileHidden_DoesNothing()
    {
        var rig = new Rig();

        await rig.Controller.Tap();

        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
        Assert.Equal(0, rig.Recorder.Starts);
    }
}
