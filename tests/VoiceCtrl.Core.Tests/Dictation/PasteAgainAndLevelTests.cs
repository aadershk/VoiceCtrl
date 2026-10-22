using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.Injection;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>Paste Again (CONTEXT) and the throttled level stream (spec §5.1).</summary>
public class PasteAgainAndLevelTests
{
    [Fact]
    public async Task PasteAgain_PastesTheLatestTranscription_WithoutStartingADictationOrShowingTheOverlay()
    {
        var rig = new Rig();
        rig.Transcriber.Returns("first");
        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();
        rig.Timer.Advance(DictationController.PastedDuration);

        await rig.Controller.PasteAgain();

        Assert.Equal(["first", "first"], rig.Injector.Pasted);
        Assert.Equal(1, rig.Recorder.Starts);
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
    }

    [Fact]
    public async Task PasteAgain_ForAChosenDictationsRow_PastesThatRowsText()
    {
        var rig = new Rig();
        rig.Last.Set("latest");

        await rig.Controller.PasteAgain("an older one");

        Assert.Equal(["an older one"], rig.Injector.Pasted);
    }

    [Fact]
    public async Task PasteAgain_BeforeAnyTranscription_DoesNothing()
    {
        var rig = new Rig();

        InjectionResult? result = await rig.Controller.PasteAgain();

        Assert.Null(result);
        Assert.Empty(rig.Injector.Pasted);
        Assert.Empty(rig.Views);
    }

    [Theory]
    [InlineData(InjectionResult.Injected)]
    [InlineData(InjectionResult.ClipboardOnlyElevatedTarget)]
    [InlineData(InjectionResult.Failed)]
    public async Task PasteAgain_SaysHowThePasteWent(InjectionResult injected)
    {
        var rig = new Rig();
        rig.Injector.Result = injected;

        Assert.Equal(injected, await rig.Controller.PasteAgain("text"));
    }

    [Fact]
    public async Task ADictation_IsKeptWithTheAppItWasFor()
    {
        var rig = new Rig();
        rig.ForegroundApp.Name = "Outlook";
        rig.Transcriber.Returns("Hi Maya");

        await rig.Controller.HoldStart();
        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Equal("Outlook", rig.History.Entries[0].AppName);
    }

    [Theory]
    [InlineData(InjectionResult.ClipboardOnlyElevatedTarget, OverlayNotice.CopiedPressCtrlV)]
    [InlineData(InjectionResult.Failed, OverlayNotice.PasteFailed)]
    public async Task PasteAgain_ThatDoesNotLand_SaysSoOnTheOverlay(InjectionResult result, OverlayNotice expected)
    {
        var rig = new Rig();
        rig.Last.Set("text");
        rig.Injector.Result = result;

        await rig.Controller.PasteAgain();

        Assert.Equal(expected, rig.Controller.View.Notice);
        rig.Timer.Advance(DictationController.NoticeDuration);
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
    }

    [Fact]
    public async Task PasteAgain_DuringADictation_IsIgnored()
    {
        var rig = new Rig();
        rig.Last.Set("text");
        await rig.Controller.HoldStart();

        await rig.Controller.PasteAgain();

        Assert.Empty(rig.Injector.Pasted);
        Assert.Equal(OverlayPhase.Listening, rig.Phase);
    }

    [Fact]
    public void CopyFailed_ShowsItsNoticeForAWhile_ThenReturnsToRest()
    {
        var rig = new Rig();

        rig.Controller.ReportCopyFailed();

        Assert.Equal(OverlayPhase.Notice, rig.Phase);
        Assert.Equal(OverlayNotice.CopyFailed, rig.Controller.View.Notice);
        rig.Timer.Advance(DictationController.NoticeDuration);
        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
    }

    [Fact]
    public async Task CopyFailed_DuringADictation_LeavesTheDictationOnScreen()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();

        rig.Controller.ReportCopyFailed();

        Assert.Equal(OverlayPhase.Listening, rig.Phase);
    }

    [Fact]
    public async Task Level_IsPassedOnOnlyWhileListening()
    {
        var rig = new Rig();
        rig.Recorder.RaiseLevel(0.5f);

        await rig.Controller.HoldStart();
        rig.Recorder.RaiseLevel(0.4f);
        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();
        rig.Timer.AdvanceMs(100);
        rig.Recorder.RaiseLevel(0.3f);

        Assert.Equal([0.4f], rig.Levels);
    }

    [Fact]
    public async Task Level_IsCappedAt60PerSecond()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();

        rig.Recorder.RaiseLevel(0.1f);
        rig.Timer.AdvanceMs(10);
        rig.Recorder.RaiseLevel(0.2f);
        rig.Timer.AdvanceMs(7);
        rig.Recorder.RaiseLevel(0.3f);

        Assert.Equal([0.1f, 0.3f], rig.Levels);
    }

    [Theory]
    [InlineData(-0.2f, 0f)]
    [InlineData(1.7f, 1f)]
    public async Task Level_IsClampedToZeroToOne(float raw, float expected)
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();

        rig.Recorder.RaiseLevel(raw);

        Assert.Equal([expected], rig.Levels);
    }
}
