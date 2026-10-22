using System.Net.Http;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Dictation;

/// <summary>The Failed Dictation half of the engine: keeping failures, Retry and Dismiss
/// (CONTEXT: Failed Dictation, Retry).</summary>
public sealed partial class DictationController
{
    /// <summary>Retry on the Overlay's Failed notice: transcribes the kept audio again and pastes
    /// the result like any Dictation. Acts only on the failure the Overlay is showing.</summary>
    public async Task Retry(Guid id, bool offline)
    {
        if (View.Phase != OverlayPhase.Failed || View.FailedId != id
            || BeginRetry(id, offline) is not ({ } failed, { } client))
        {
            return;
        }

        await RunOverlayRetryAsync(failed, client).ConfigureAwait(true);
    }

    /// <summary>Transcribes a claimed failure's kept audio again and pastes the result, with the
    /// Overlay showing Transcribing. Shared by Retry and Undo.</summary>
    private async Task RunOverlayRetryAsync(FailedDictation failed, ITranscriptionClient client)
    {
        var dictation = new ActiveDictation(_collaborators.Timer.UtcNow, startedByHold: false, failed.AppName)
        {
            IsListening = false,
            RetryOf = failed,
        };
        _active = dictation;
        BeginTranscribing(dictation);

        try
        {
            await TranscribeAndPasteAsync(dictation, failed.Audio, failed.AudioLength, client).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("RetryOnOverlay", ex);
            if (ReferenceEquals(_active, dictation))
            {
                _active = null;
                ReturnToRest();
            }
        }
    }

    /// <summary>"Retry offline" is offered: an offline client exists and its model is on disk.</summary>
    public bool CanRetryOffline =>
        _collaborators.OfflineTranscription is not null && _collaborators.IsOfflineModelInstalled();

    /// <summary>
    /// Retry from the Dictations screen. It never pastes (the user is in the Hub, not the target
    /// app) and never touches the Overlay. The result becomes the latest Dictation, stamped with
    /// the time it was transcribed, so it is the one Paste Again pastes and the one at the top.
    /// </summary>
    public async Task<RetryOutcome> RetryFromDictations(Guid id, bool offline)
    {
        if (BeginRetry(id, offline) is not ({ } failed, { } client))
        {
            return RetryOutcome.Ignored;
        }

        string? text;
        string? original;
        try
        {
            text = await client.TranscribeAsync(failed.Audio).ConfigureAwait(true);
            original = _collaborators.TakeOriginal();
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("RetryFromDictations", ex);
            failed.Reason = Classify(ex);
            failed.IsRetrying = false;
            _failed.RaiseChanged();
            return RetryOutcome.Failed;
        }

        failed.IsRetrying = false;
        RemoveFailed(failed);
        if (string.IsNullOrWhiteSpace(text))
        {
            return RetryOutcome.NoSpeech;
        }

        _collaborators.LastTranscription.Set(text);
        _collaborators.History.Add(text, failed.AppName, original);
        return RetryOutcome.Transcribed;
    }

    /// <summary>✕ on the Failed notice or a Dictations row: drops the kept audio for good.</summary>
    public void DismissFailed(Guid id)
    {
        if (_failed.Find(id) is { } failed)
        {
            RemoveFailed(failed);
        }
    }

    /// <summary>Keeps the audio (or, for a failed Retry, updates the kept entry) and shows the
    /// Failed notice for it.</summary>
    private void ShowFailed(ActiveDictation dictation, byte[] audio, TimeSpan audioLength, FailureReason reason)
    {
        FailedDictation failed;
        if (dictation.RetryOf is { } retried)
        {
            failed = retried;
            failed.Reason = reason;
            failed.IsRetrying = false;
            _failed.RaiseChanged();
        }
        else
        {
            failed = new FailedDictation(Guid.NewGuid(), audio, audioLength, reason, _collaborators.Timer.UtcNow, dictation.AppName);
            _failed.Add(failed);
        }

        _collaborators.SoundCues.Play(SoundCue.Failed);
        ShowForAWhile(
            new OverlayView
            {
                Phase = OverlayPhase.Failed,
                Controls = OverlayControls.Retry | OverlayControls.Dismiss,
                FailureReason = reason,
                FailedId = failed.Id,
            },
            FailedDuration);
    }

    /// <summary>Claims a Retry of <paramref name="id"/>: null when it isn't kept, is already
    /// being retried, or asks for an offline model that isn't installed.</summary>
    private (FailedDictation Failed, ITranscriptionClient Client)? BeginRetry(Guid id, bool offline)
    {
        ITranscriptionClient? client = !offline ? _collaborators.Transcription
            : CanRetryOffline ? _collaborators.OfflineTranscription
            : null;
        if (_failed.Find(id) is not { IsRetrying: false } failed || client is null)
        {
            return null;
        }

        failed.IsRetrying = true;
        _failed.RaiseChanged();
        return (failed, client);
    }

    /// <summary>Removes a kept failure; if the Overlay is showing it, the notice goes too.</summary>
    private void RemoveFailed(FailedDictation failed)
    {
        failed.Expiry?.Dispose();
        _failed.Remove(failed);
        if (View.Phase == OverlayPhase.Failed && View.FailedId == failed.Id)
        {
            ReturnToRest();
        }
    }

    /// <summary>Maps a transcription failure to the reason the Overlay names. Anything that isn't
    /// recognisably the network or a timeout is the service's fault as far as the user can act.</summary>
    private static FailureReason Classify(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case HttpRequestException:
                    return FailureReason.NoInternet;
                case TaskCanceledException or TimeoutException:
                    return FailureReason.TimedOut;
            }
        }

        return FailureReason.ServiceError;
    }
}
