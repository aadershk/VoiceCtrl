using VoiceCtrl.Core.Dictation;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>"Always show the Overlay" (DESIGN §9): the Overlay rests as the grey handle instead of
/// hiding, and the handle's Ready tile starts a Dictation that ends back on the handle.</summary>
public class AlwaysShowTests
{
    [Fact]
    public void TurningItOn_WhileHidden_ShowsTheHandle_AndTurningItOffHidesIt()
    {
        var rig = new Rig();

        rig.Controller.AlwaysShow = true;
        Assert.Equal(OverlayPhase.Handle, rig.Phase);
        Assert.Equal(OverlayControls.Start, rig.Controller.View.Controls);

        rig.Controller.AlwaysShow = false;
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
    }

    [Fact]
    public async Task TurningItOn_DuringADictation_LeavesTheDictationAlone_AndItEndsOnTheHandle()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();

        rig.Controller.AlwaysShow = true;
        Assert.Equal(OverlayPhase.Listening, rig.Phase);

        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();
        rig.Timer.Advance(DictationController.PastedDuration);
        Assert.Equal(OverlayPhase.Handle, rig.Phase);
    }

    [Fact]
    public async Task StartFromTheHandle_Listens_AndPastingReturnsToTheHandle()
    {
        var rig = new Rig();
        rig.Controller.AlwaysShow = true;

        await rig.Controller.StartFromOverlay();
        Assert.Equal(OverlayPhase.Listening, rig.Phase);
        Assert.Equal([SoundCue.Start], rig.Cues.Played);

        await rig.Controller.Tap();
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);

        rig.Timer.Advance(DictationController.PastedDuration);
        Assert.Equal(OverlayPhase.Handle, rig.Phase);
    }

    [Fact]
    public async Task CancelFromTheHandle_ReturnsToTheHandle_WithTheThrownAwayExit()
    {
        var rig = new Rig();
        rig.Controller.AlwaysShow = true;
        await rig.Controller.StartFromOverlay();

        rig.Controller.Cancel();

        Assert.Equal(OverlayPhase.Handle, rig.Phase);
        Assert.True(rig.Controller.View.AfterCancel);
    }

    [Fact]
    public async Task TriggerKeyTap_OnTheHandle_DoesNothing()
    {
        // Only the Ready tile starts a Dictation on a tap; the handle is a resting Overlay, and a
        // stray Trigger Key tap shouldn't start recording just because it's on screen.
        var rig = new Rig();
        rig.Controller.AlwaysShow = true;

        await rig.Controller.Tap();

        Assert.Equal(OverlayPhase.Handle, rig.Phase);
        Assert.False(rig.Recorder.IsRecording);
    }

    [Fact]
    public async Task HotkeyDoubleTap_OnTheHandle_ShowsTheReadyTile_AndASecondOneGoesBackToTheHandle()
    {
        var rig = new Rig();
        rig.Controller.AlwaysShow = true;

        await rig.Controller.ToggleBar();
        Assert.Equal(OverlayPhase.Ready, rig.Phase);

        await rig.Controller.ToggleBar();
        Assert.Equal(OverlayPhase.Handle, rig.Phase);
    }

    [Fact]
    public async Task StartFromOverlay_OnTheReadyTile_IsATap_AndEndsOnTheReadyTile()
    {
        var rig = new Rig();
        await rig.Controller.ToggleBar();

        await rig.Controller.StartFromOverlay();
        Assert.Equal(OverlayPhase.Listening, rig.Phase);

        await rig.Controller.Tap();
        rig.Timer.Advance(DictationController.PastedDuration);
        Assert.Equal(OverlayPhase.Ready, rig.Phase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartFromOverlay_DoesNothing_WhenTheOverlayOffersNoStart(bool alwaysShow)
    {
        var rig = new Rig();
        rig.Controller.AlwaysShow = alwaysShow;
        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);

        await rig.Controller.StartFromOverlay();

        Assert.Equal(OverlayPhase.Pasted, rig.Phase);
        Assert.False(rig.Recorder.IsRecording);
    }

    [Fact]
    public async Task StartFromOverlay_WhileHidden_DoesNothing()
    {
        var rig = new Rig();

        await rig.Controller.StartFromOverlay();

        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
        Assert.False(rig.Recorder.IsRecording);
    }
}
