using VoiceCtrl.Core.Dictation;

namespace VoiceCtrl.Core.Tests.Dictation;

/// <summary>Failed Dictations, Retry and Dismiss (spec §5.1; CONTEXT: Failed Dictation, Retry).</summary>
public class FailedDictationTests
{
    public static TheoryData<Exception, FailureReason> Failures => new()
    {
        { Rig.NoInternet(), FailureReason.NoInternet },
        { Rig.TimedOut(), FailureReason.TimedOut },
        { Rig.ServiceError(), FailureReason.ServiceError },
        { new InvalidOperationException("unforeseen"), FailureReason.ServiceError },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task AFailedTranscription_ShowsFailedWithItsReason_AndKeepsTheAudio(Exception failure, FailureReason reason)
    {
        var rig = new Rig();

        Guid id = await rig.FailOneAsync(failure);

        OverlayView view = rig.Controller.View;
        Assert.Equal(OverlayPhase.Failed, view.Phase);
        Assert.Equal(reason, view.FailureReason);
        Assert.Equal(id, view.FailedId);
        Assert.Equal(OverlayControls.Retry | OverlayControls.Dismiss, view.Controls);
        FailedDictation kept = Assert.Single(rig.Controller.FailedDictations);
        Assert.Equal(reason, kept.Reason);
        Assert.Equal("Notepad", kept.AppName);
        Assert.Equal(SoundCue.Failed, rig.Cues.Played[^1]);
        Assert.Empty(rig.Injector.Pasted);
    }

    [Fact]
    public async Task TheFailedNotice_LeavesAfter8s_ButTheFailedDictationIsStillKept()
    {
        var rig = new Rig();
        await rig.FailOneAsync();

        rig.Timer.AdvanceMs(7999);
        Assert.Equal(OverlayPhase.Failed, rig.Phase);
        rig.Timer.AdvanceMs(1);

        Assert.Equal(OverlayPhase.Ready, rig.Phase);
        Assert.Single(rig.Controller.FailedDictations);
    }

    [Fact]
    public async Task UpToFiveAreKept_NewestFirst_AndASixthPushesOutTheOldest()
    {
        var rig = new Rig();
        var ids = new List<Guid>();
        for (int i = 0; i < 6; i++)
        {
            ids.Add(await rig.FailOneAsync());
        }

        Assert.Equal(Enumerable.Reverse(ids).Take(5), rig.Controller.FailedDictations.Select(f => f.Id));
        Assert.Equal(ids[^1], rig.Controller.View.FailedId);
    }

    [Fact]
    public async Task RetryOnTheOverlay_TranscribesTheKeptAudioAgain_AndPastes()
    {
        var rig = new Rig();
        Guid id = await rig.FailOneAsync();
        byte[] keptAudio = rig.Transcriber.Calls[0];
        rig.Transcriber.Returns("second time lucky");

        await rig.Controller.Retry(id, offline: false);

        Assert.Same(keptAudio, rig.Transcriber.Calls[1]);
        Assert.Equal(["second time lucky"], rig.Injector.Pasted);
        Assert.Equal(OverlayPhase.Pasted, rig.Phase);
        Assert.Empty(rig.Controller.FailedDictations);
    }

    [Fact]
    public async Task ARetryThatFailsAgain_KeepsTheSameFailedDictation_WithTheNewReason()
    {
        var rig = new Rig();
        Guid id = await rig.FailOneAsync(Rig.NoInternet());
        rig.Transcriber.Throws(Rig.TimedOut());

        await rig.Controller.Retry(id, offline: false);

        FailedDictation kept = Assert.Single(rig.Controller.FailedDictations);
        Assert.Equal(id, kept.Id);
        Assert.Equal(FailureReason.TimedOut, kept.Reason);
        Assert.Equal(OverlayPhase.Failed, rig.Phase);
        Assert.Equal(FailureReason.TimedOut, rig.Controller.View.FailureReason);
    }

    [Fact]
    public async Task RetryOffline_UsesTheOfflineModel()
    {
        var rig = new Rig();
        Guid id = await rig.FailOneAsync();

        await rig.Controller.RetryFromDictations(id, offline: true);

        Assert.Single(rig.OfflineTranscriber.Calls);
        Assert.Single(rig.Transcriber.Calls);
        Assert.Empty(rig.Controller.FailedDictations);
    }

    [Fact]
    public async Task RetryOffline_WithNoOfflineModelInstalled_DoesNothing()
    {
        var rig = new Rig(offlineInstalled: false);
        Guid id = await rig.FailOneAsync();

        await rig.Controller.RetryFromDictations(id, offline: true);

        Assert.Single(rig.Transcriber.Calls);
        Assert.Single(rig.Controller.FailedDictations);
        Assert.False(rig.Controller.FailedDictations[0].IsRetrying);
    }

    [Fact]
    public async Task RetryFromTheDictationsScreen_NeverPastes_AndTheResultEntersTheList()
    {
        var rig = new Rig();
        Guid id = await rig.FailOneAsync();
        rig.Timer.AdvanceMs(8000);
        rig.Transcriber.Returns("recovered");

        await rig.Controller.RetryFromDictations(id, offline: false);

        Assert.Empty(rig.Injector.Pasted);
        Assert.Equal("recovered", rig.History.Entries[0].Text);
        Assert.Equal("recovered", rig.Last.Text);
        Assert.Empty(rig.Controller.FailedDictations);
        Assert.Equal(OverlayPhase.Ready, rig.Phase);
    }

    [Fact]
    public async Task RetryFromTheDictationsScreen_ThatFailsAgain_UpdatesTheReason_WithoutShowingTheOverlay()
    {
        var rig = new Rig();
        Guid id = await rig.FailOneAsync(Rig.NoInternet());
        rig.Timer.AdvanceMs(8000);
        rig.Transcriber.Throws(Rig.ServiceError());

        await rig.Controller.RetryFromDictations(id, offline: false);

        Assert.Equal(FailureReason.ServiceError, rig.Controller.FailedDictations[0].Reason);
        Assert.Equal(OverlayPhase.Ready, rig.Phase);
    }

    [Fact]
    public async Task Dismiss_DropsTheAudio_AndTheOverlayReturnsToRest()
    {
        var rig = new Rig();
        Guid id = await rig.FailOneAsync();
        int changes = 0;
        rig.Controller.FailedDictationsChanged += () => changes++;

        rig.Controller.DismissFailed(id);

        Assert.Empty(rig.Controller.FailedDictations);
        Assert.Equal(1, changes);
        Assert.Equal(OverlayPhase.Ready, rig.Phase);
    }

    [Fact]
    public async Task CancellingAnOverlayRetry_StopsIt_ButKeepsTheFailedDictation()
    {
        var rig = new Rig();
        Guid id = await rig.FailOneAsync();
        rig.Transcriber.Hold = true;
        Task retrying = rig.Controller.Retry(id, offline: false);
        Assert.Equal(OverlayPhase.Transcribing, rig.Phase);

        rig.Controller.Cancel();
        await retrying;

        Assert.Single(rig.Controller.FailedDictations);
        Assert.False(rig.Controller.FailedDictations[0].IsRetrying);
        Assert.Equal(OverlayPhase.Ready, rig.Phase);
    }

    [Fact]
    public async Task ASecondRetryOfTheSameFailure_WhileTheFirstRuns_IsIgnored()
    {
        var rig = new Rig();
        Guid id = await rig.FailOneAsync();
        rig.Timer.AdvanceMs(8000);
        rig.Transcriber.Hold = true;
        Task first = rig.Controller.RetryFromDictations(id, offline: false);

        await rig.Controller.RetryFromDictations(id, offline: false);
        Assert.Equal(2, rig.Transcriber.Calls.Count);

        rig.Transcriber.Complete("ok");
        await first;
    }

    [Fact]
    public async Task TheOverlayRetry_OnlyActsOnTheFailureTheOverlayIsShowing()
    {
        var rig = new Rig();
        Guid id = await rig.FailOneAsync();
        rig.Timer.AdvanceMs(8000);

        await rig.Controller.Retry(id, offline: false);

        Assert.Single(rig.Transcriber.Calls);
        Assert.Equal(OverlayPhase.Ready, rig.Phase);
    }

    [Fact]
    public async Task AKeptFailure_KnowsItsAppAndHowMuchAudioItHolds()
    {
        var rig = new Rig();
        rig.ForegroundApp.Name = "Slack";
        rig.Recorder.ClipLength = TimeSpan.FromSeconds(12);

        await rig.FailOneAsync();

        FailedDictation kept = rig.Controller.FailedDictations[0];
        Assert.Equal("Slack", kept.AppName);
        Assert.Equal(TimeSpan.FromSeconds(12), kept.AudioLength);
    }

    [Fact]
    public async Task RetryFromTheDictationsScreen_SaysHowItWent_AndTheResultKeepsItsApp()
    {
        var rig = new Rig();
        rig.ForegroundApp.Name = "Slack";
        Guid id = await rig.FailOneAsync();
        rig.ForegroundApp.Name = "VoiceCtrl";
        rig.Transcriber.Returns("recovered");

        RetryOutcome outcome = await rig.Controller.RetryFromDictations(id, offline: false);

        Assert.Equal(RetryOutcome.Transcribed, outcome);
        Assert.Equal("Slack", rig.History.Entries[0].AppName);
    }

    [Fact]
    public async Task RetryFromTheDictationsScreen_ThatFindsNoSpeech_DropsTheFailure()
    {
        var rig = new Rig();
        Guid id = await rig.FailOneAsync();
        rig.Transcriber.Returns("");

        RetryOutcome outcome = await rig.Controller.RetryFromDictations(id, offline: false);

        Assert.Equal(RetryOutcome.NoSpeech, outcome);
        Assert.Empty(rig.Controller.FailedDictations);
        Assert.Empty(rig.History.Entries);
    }

    [Fact]
    public async Task RetryFromTheDictationsScreen_ThatFailsAgain_OrCannotRun_SaysSo()
    {
        var rig = new Rig();
        Guid id = await rig.FailOneAsync();
        rig.Transcriber.Throws(Rig.TimedOut());

        Assert.Equal(RetryOutcome.Failed, await rig.Controller.RetryFromDictations(id, offline: false));
        Assert.Equal(RetryOutcome.Ignored, await rig.Controller.RetryFromDictations(Guid.NewGuid(), offline: false));
    }

    [Fact]
    public async Task RetryOffline_IsOfferedOnlyWhileAnOfflineModelIsInstalled()
    {
        var rig = new Rig();
        Guid id = await rig.FailOneAsync();
        Assert.True(rig.Controller.CanRetryOffline);

        rig.OfflineModelInstalled = false;

        Assert.False(rig.Controller.CanRetryOffline);
        Assert.Equal(RetryOutcome.Ignored, await rig.Controller.RetryFromDictations(id, offline: true));
        Assert.False(new Rig(offlineInstalled: false).Controller.CanRetryOffline);
    }
}
