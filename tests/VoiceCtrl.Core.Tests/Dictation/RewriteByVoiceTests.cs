using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Rewrite;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>Rewrite by voice (ticket 39): hold the second key, say what to do to the selected text
/// (or, with nothing selected, to the last Dictation), and the result replaces it.</summary>
public class RewriteByVoiceTests
{
    /// <summary>Holds the Rewrite key, speaks, and lets go.</summary>
    private static async Task<Rig> RewriteAsync(string? selected = "The quick brown fox.", Action<Rig>? arrange = null)
    {
        var rig = new Rig();
        rig.Selection.Selected = selected;
        arrange?.Invoke(rig);
        await rig.Controller.RewriteHoldStart();
        rig.Timer.AdvanceMs(600); await rig.Controller.RewriteHoldEnd();
        return rig;
    }

    [Fact]
    public async Task Hold_ShowsAListeningOverlayMarkedAsARewrite()
    {
        var rig = new Rig();

        await rig.Controller.RewriteHoldStart();

        Assert.Equal(OverlayPhase.Listening, rig.Phase);
        Assert.True(rig.Controller.View.Rewrite);
        Assert.Equal(1, rig.Rewriter.Prewarms);
        Assert.Empty(rig.Transcriber.Calls);
    }

    [Fact]
    public async Task Release_ShowsTranscribingMarkedAsARewrite_UntilTheRewriteArrives()
    {
        var rig = new Rig();
        rig.Selection.Selected = "text";
        await rig.Controller.RewriteHoldStart();

        rig.Timer.AdvanceMs(600); Task ending = rig.Controller.RewriteHoldEnd();

        Assert.Contains(rig.Views, v => v.Phase == OverlayPhase.Transcribing && v.Rewrite);
        await ending;
    }

    [Fact]
    public async Task Selection_IsRewrittenAndPastedOverIt()
    {
        Rig rig = await RewriteAsync("The quick brown fox.");

        Assert.Equal([("The quick brown fox.")], rig.Rewriter.Calls.Select(c => c.Source));
        Assert.Equal(["REWRITTEN"], rig.Injector.Pasted);
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);
        Assert.Equal([SoundCue.Start, SoundCue.Stop, SoundCue.Done], rig.Cues.Played);
    }

    [Fact]
    public async Task Selection_SendsTheRecordedClipToTheRewriter()
    {
        Rig rig = await RewriteAsync();

        Assert.Single(rig.Rewriter.Calls);
        Assert.NotEmpty(rig.Rewriter.Calls[0].Audio);
        Assert.Empty(rig.Transcriber.Calls);
    }

    [Fact]
    public async Task Rewrite_IsKeptWithTheTextBeforeAsItsOriginal()
    {
        Rig rig = await RewriteAsync("The quick brown fox.");

        Assert.Equal("REWRITTEN", rig.Last.Text);
        Assert.Equal("REWRITTEN", rig.History.Entries[0].Text);
        Assert.Equal("The quick brown fox.", rig.History.Entries[0].Original);
        Assert.Equal("Notepad", rig.History.Entries[0].AppName);
    }

    [Fact]
    public async Task NothingSelected_RewritesTheLastDictationInPlace()
    {
        Rig rig = await RewriteAsync(selected: null, rig => rig.Last.Set("I just dictated this."));

        Assert.Equal(["I just dictated this."], rig.Rewriter.Calls.Select(c => c.Source));
        Assert.Equal(["copy", "select:I just dictated this."], rig.Selection.Asked);
        Assert.Equal(["REWRITTEN"], rig.Injector.Pasted);
    }

    [Fact]
    public async Task NothingSelected_AndTheLastDictationIsNotBehindTheCaret_ChangesNothing()
    {
        Rig rig = await RewriteAsync(selected: null, rig =>
        {
            rig.Last.Set("I just dictated this.");
            rig.Selection.SelectPrecedingResult = false;
        });

        Assert.Empty(rig.Injector.Pasted);
        Assert.Equal(OverlayPhase.Notice, rig.Phase);
        Assert.Equal(OverlayNotice.NothingToRewrite, rig.Controller.View.Notice);
        Assert.Equal("I just dictated this.", rig.Last.Text);
    }

    [Fact]
    public async Task NothingSelected_AndNothingDictatedYet_SaysSelectSomething_WithoutCallingTheRewriter()
    {
        Rig rig = await RewriteAsync(selected: null);

        Assert.Equal(OverlayNotice.NothingToRewrite, rig.Controller.View.Notice);
        Assert.Empty(rig.Rewriter.Calls);
        Assert.Empty(rig.Injector.Pasted);
    }

    [Fact]
    public async Task NothingSelected_TheLastDictationIsOnlySelectedOnceTheRewriteIsReady()
    {
        Rig rig = await RewriteAsync(selected: null, rig =>
        {
            rig.Last.Set("I just dictated this.");
            rig.Rewriter.Outcome = RewriteOutcome.CouldNotRewrite;
        });

        // A failed rewrite must not leave the user's text highlighted, ready to be typed over.
        Assert.Equal(["copy"], rig.Selection.Asked);
    }

    [Fact]
    public async Task NothingSelected_ARewriteRewritesTheLastRewriteNext()
    {
        Rig rig = await RewriteAsync(selected: null, rig => rig.Last.Set("first"));
        rig.Rewriter.Outcome = RewriteOutcome.Rewritten("SECOND");
        await rig.Controller.RewriteHoldStart();
        rig.Timer.AdvanceMs(600); await rig.Controller.RewriteHoldEnd();

        Assert.Equal(["first", "REWRITTEN"], rig.Rewriter.Calls.Select(c => c.Source));
    }

    [Fact]
    public async Task NoInstructionHeard_ShowsNoSpeech_AndChangesNothing()
    {
        Rig rig = await RewriteAsync(arrange: rig => rig.Rewriter.Outcome = RewriteOutcome.NoInstruction);

        Assert.Equal(OverlayPhase.NoSpeech, rig.Phase);
        Assert.Empty(rig.Injector.Pasted);
    }

    [Fact]
    public async Task SilentClip_ShowsNoSpeech_WithoutReadingTheSelection()
    {
        Rig rig = await RewriteAsync(arrange: rig => rig.Recorder.ClipLength = TimeSpan.FromMilliseconds(10));

        Assert.Equal(OverlayPhase.NoSpeech, rig.Phase);
        Assert.Empty(rig.Selection.Asked);
        Assert.Empty(rig.Rewriter.Calls);
    }

    [Fact]
    public async Task NoLanguageModelOffline_SaysSo()
    {
        Rig rig = await RewriteAsync(arrange: rig => rig.Rewriter.Outcome = RewriteOutcome.NeedsLanguageModel);

        Assert.Equal(OverlayNotice.RewriteNeedsLanguageModel, rig.Controller.View.Notice);
        Assert.Empty(rig.Injector.Pasted);
    }

    [Fact]
    public async Task ARewriteThatCouldNotBeMade_LeavesTheTextAndSaysSo()
    {
        Rig rig = await RewriteAsync(arrange: rig => rig.Rewriter.Outcome = RewriteOutcome.CouldNotRewrite);

        Assert.Equal(OverlayNotice.RewriteFailed, rig.Controller.View.Notice);
        Assert.Empty(rig.Injector.Pasted);
        Assert.Empty(rig.History.Entries);
    }

    [Fact]
    public async Task AFailingService_LeavesTheTextAndSaysSo()
    {
        Rig rig = await RewriteAsync(arrange: rig => rig.Rewriter.Throws = Rig.NoInternet());

        Assert.Equal(OverlayNotice.RewriteFailed, rig.Controller.View.Notice);
        Assert.Empty(rig.Injector.Pasted);
    }

    [Fact]
    public async Task APasteThatDidNotLand_IsSaidAndTheRewriteIsStillKept()
    {
        Rig rig = await RewriteAsync(arrange: rig => rig.Injector.Result = InjectionResult.Failed);

        Assert.Equal(OverlayNotice.PasteFailed, rig.Controller.View.Notice);
        Assert.Equal("REWRITTEN", rig.Last.Text);
    }

    [Fact]
    public async Task ElevatedTarget_CopiesInsteadAndSaysPressCtrlV()
    {
        Rig rig = await RewriteAsync(arrange: rig => rig.Injector.Result = InjectionResult.ClipboardOnlyElevatedTarget);

        Assert.Equal(OverlayNotice.CopiedPressCtrlV, rig.Controller.View.Notice);
    }

    [Fact]
    public async Task MissingApiKey_AsksForOne_AsADictationDoes()
    {
        Rig rig = await RewriteAsync(arrange: rig => rig.ApiKeyMissing = true);

        Assert.Equal(OverlayNotice.AddApiKey, rig.Controller.View.Notice);
        Assert.Empty(rig.Rewriter.Calls);
    }

    [Fact]
    public async Task Esc_WhileListening_ThrowsTheRewriteAway()
    {
        var rig = new Rig();
        rig.Selection.Selected = "text";
        await rig.Controller.RewriteHoldStart();

        rig.Controller.Cancel();
        rig.Timer.AdvanceMs(600); await rig.Controller.RewriteHoldEnd();

        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
        Assert.Empty(rig.Rewriter.Calls);
        Assert.Empty(rig.Injector.Pasted);
    }

    [Fact]
    public async Task CancelRewrite_WhileListening_ThrowsTheRewriteAway()
    {
        var rig = new Rig();
        await rig.Controller.RewriteHoldStart();

        rig.Controller.CancelRewrite();
        rig.Timer.AdvanceMs(600); await rig.Controller.RewriteHoldEnd();

        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
        Assert.Empty(rig.Rewriter.Calls);
    }

    [Fact]
    public async Task CancelRewrite_DoesNotCancelADictation()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();

        rig.Controller.CancelRewrite();

        Assert.Equal(OverlayPhase.Listening, rig.Phase);
    }

    [Fact]
    public async Task CancelRewrite_AfterTheInstructionWasSent_LeavesItRunning()
    {
        var rig = new Rig();
        rig.Selection.Selected = "text";
        await rig.Controller.RewriteHoldStart();
        rig.Timer.AdvanceMs(600); Task ending = rig.Controller.RewriteHoldEnd();

        rig.Controller.CancelRewrite();
        await ending;

        Assert.Equal(["REWRITTEN"], rig.Injector.Pasted);
    }

    [Fact]
    public async Task TheDictationKeyRelease_DoesNotEndARewriteHold()
    {
        var rig = new Rig();
        await rig.Controller.RewriteHoldStart();

        rig.Timer.AdvanceMs(600); await rig.Controller.HoldEnd();

        Assert.Equal(OverlayPhase.Listening, rig.Phase);
    }

    [Fact]
    public async Task TheRewriteKeyRelease_DoesNotEndADictationHold()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();

        rig.Timer.AdvanceMs(600); await rig.Controller.RewriteHoldEnd();

        Assert.Equal(OverlayPhase.Listening, rig.Phase);
        Assert.False(rig.Controller.View.Rewrite);
    }

    [Fact]
    public async Task RewriteHold_WhileADictationIsListening_IsIgnored()
    {
        var rig = new Rig();
        await rig.Controller.HoldStart();

        await rig.Controller.RewriteHoldStart();

        Assert.Equal(1, rig.Recorder.Starts);
        Assert.False(rig.Controller.View.Rewrite);
    }

    [Fact]
    public async Task WithoutARewriter_TheHoldDoesNothing()
    {
        var rig = new Rig(rewriteAvailable: false);

        await rig.Controller.RewriteHoldStart();

        Assert.Equal(OverlayPhase.Hidden, rig.Phase);
        Assert.Equal(0, rig.Recorder.Starts);
    }
}
