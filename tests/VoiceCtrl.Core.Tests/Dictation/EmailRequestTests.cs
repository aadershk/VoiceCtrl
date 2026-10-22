using VoiceCtrl.Core.Dictation;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>An Email request (ticket 57): the Rewrite key, nothing selected, in an email app,
/// "write an email to Mark thanking him for his time", and the email is written at the caret.
/// Plain dictation is never read for requests.</summary>
public class EmailRequestTests
{
    private const string Request = "Write an email to Mark thanking him for his time.";
    private const string Email = "Dear Mark,\n\nThank you for your time.\n\nKind regards,\nAlex";

    /// <summary>Holds the Rewrite key with nothing selected in an email app, says <paramref name="said"/>
    /// and lets go; the task is the rewrite's end.</summary>
    private static async Task<(Rig Rig, Task Ending)> AskAsync(string said, Action<Rig>? arrange = null)
    {
        var rig = new Rig();
        rig.Selection.Selected = null;
        rig.EmailWriter.InAnEmailApp = true;
        rig.EmailWriter.RequestWords = Request;
        rig.Rewriter.Heard = said;
        arrange?.Invoke(rig);
        await rig.Controller.RewriteHoldStart();
        rig.Timer.AdvanceMs(600);
        return (rig, rig.Controller.RewriteHoldEnd());
    }

    [Fact]
    public async Task ARequest_IsWrittenAtTheCaret_WithTheWordsSaidAsItsOriginal()
    {
        (Rig rig, Task ending) = await AskAsync(Request, r => r.Last.Set("An earlier dictation."));
        await ending;

        Assert.Equal([Email], rig.Injector.Pasted);
        Assert.Equal(Email, rig.History.Entries[0].Text);
        Assert.Equal(Request, rig.History.Entries[0].Original);
        Assert.Equal(Email, rig.Last.Text);
        Assert.Empty(rig.Rewriter.Calls);
        Assert.DoesNotContain("select:An earlier dictation.", rig.Selection.Asked);
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);
    }

    [Fact]
    public async Task ARequest_NeedsNoEarlierDictation()
    {
        (Rig rig, Task ending) = await AskAsync(Request);
        await ending;

        Assert.Equal([Email], rig.Injector.Pasted);
    }

    [Fact]
    public async Task AnEdit_StillRewritesTheLastDictation_WithTheWordsAlreadyHeard()
    {
        (Rig rig, Task ending) = await AskAsync("make it shorter", r => r.Last.Set("A long dictation."));
        await ending;

        Assert.Equal(1, rig.Rewriter.Hearings);
        Assert.Equal([("A long dictation.")], rig.Rewriter.Calls.Select(c => c.Source));
        Assert.Equal(["make it shorter"], rig.Rewriter.HeardPassed);
        Assert.Empty(rig.EmailWriter.Writes);
    }

    [Fact]
    public async Task WithASelection_TheInstructionIsNeverReadForARequest()
    {
        (Rig rig, Task ending) = await AskAsync(Request, r => r.Selection.Selected = "Some selected text.");
        await ending;

        Assert.Equal(0, rig.Rewriter.Hearings);
        Assert.Empty(rig.EmailWriter.Writes);
        Assert.Equal(["REWRITTEN"], rig.Injector.Pasted);
    }

    [Fact]
    public async Task OutsideAnEmailApp_TheRewriteKeyWorksAsBefore()
    {
        (Rig rig, Task ending) = await AskAsync(Request, r =>
        {
            r.EmailWriter.InAnEmailApp = false;
            r.Last.Set("A dictation.");
        });
        await ending;

        Assert.Equal(0, rig.Rewriter.Hearings);
        Assert.Equal([null], rig.Rewriter.HeardPassed);
    }

    [Fact]
    public async Task PlainDictation_IsNeverReadForARequest()
    {
        var rig = new Rig();
        rig.EmailWriter.InAnEmailApp = true;
        rig.EmailWriter.RequestWords = Request;
        await rig.Controller.ToggleBar();
        rig.Transcriber.Returns(Request);
        await rig.Controller.Tap();
        await rig.Controller.Tap();

        Assert.Equal([Request], rig.Injector.Pasted);
        Assert.Empty(rig.EmailWriter.Writes);
        Assert.Equal(0, rig.EmailWriter.Prewarms);
    }

    [Fact]
    public async Task WhileTheEmailIsWritten_ThePillSaysSo()
    {
        (Rig rig, Task ending) = await AskAsync(Request, r => r.EmailWriter.Hold = true);

        Assert.Equal(OverlayPhase.Transcribing, rig.Phase);
        Assert.True(rig.Controller.View.Writing);
        Assert.Empty(rig.Injector.Pasted);

        rig.EmailWriter.Complete(Email);
        await ending;
        Assert.Equal([Email], rig.Injector.Pasted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnEmailThatCouldNotBeWritten_PastesNothing_AndSaysSo(bool throws)
    {
        (Rig rig, Task ending) = await AskAsync(Request, r =>
        {
            r.EmailWriter.Email = null;
            r.EmailWriter.Throws = throws ? new InvalidOperationException("model crashed") : null;
        });
        await ending;

        Assert.Empty(rig.Injector.Pasted);
        Assert.Empty(rig.History.Entries);
        Assert.Equal(OverlayNotice.EmailNotWritten, rig.Controller.View.Notice);
    }

    [Fact]
    public async Task NoWordsHeard_IsNoSpeech()
    {
        (Rig rig, Task ending) = await AskAsync(Request, r => r.Rewriter.Heard = null);
        await ending;

        Assert.Equal(OverlayPhase.NoSpeech, rig.Phase);
        Assert.Empty(rig.Injector.Pasted);
    }

    [Fact]
    public async Task Esc_WhileWriting_PastesNothing()
    {
        (Rig rig, Task ending) = await AskAsync(Request, r => r.EmailWriter.Hold = true);

        rig.Controller.Cancel();
        await ending;

        Assert.Empty(rig.Injector.Pasted);
        Assert.Empty(rig.History.Entries);
    }

    [Fact]
    public async Task APress_WhileWriting_SaysStillWriting_ThenReturnsToTheWritingPill()
    {
        (Rig rig, Task ending) = await AskAsync(Request, r => r.EmailWriter.Hold = true);

        await rig.Controller.PillPressed();
        Assert.Equal(OverlayNotice.StillWriting, rig.Controller.View.Notice);

        rig.Timer.Advance(DictationController.NoticeDuration);
        Assert.Equal(OverlayPhase.Transcribing, rig.Phase);
        Assert.True(rig.Controller.View.Writing);

        rig.EmailWriter.Complete(Email);
        await ending;
    }

    [Fact]
    public async Task Writing_GetsItsOwnLongerClock_BeforeTakingLonger()
    {
        (Rig rig, Task ending) = await AskAsync(Request, r => r.EmailWriter.Hold = true);

        rig.Timer.Advance(DictationController.SlowThreshold);
        Assert.True(rig.Controller.View.Writing);

        rig.Timer.Advance(DictationController.WritingSlowThreshold - DictationController.SlowThreshold);
        Assert.Equal(OverlayNotice.TakingLonger, rig.Controller.View.Notice);

        rig.EmailWriter.Complete(Email);
        await ending;
        Assert.Equal([Email], rig.Injector.Pasted);
    }

    [Fact]
    public async Task TheRewriteKey_WarmsTheWriterUp()
    {
        var rig = new Rig();

        await rig.Controller.RewriteHoldStart();

        Assert.Equal(1, rig.EmailWriter.Prewarms);
        Assert.Equal(1, rig.Rewriter.Prewarms);
    }
}
