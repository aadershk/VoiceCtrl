using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Dictation;

/// <summary>
/// Cancel safety and the recording cap (VED-76, ADR-0004). A cancelled recording of more than 3 s
/// is kept in memory as a "Cancelled" entry beside the Failed Dictations, with an Undo notice on
/// the pill and "Recover text" on the Dictations screen; a recording that reaches 20 minutes is
/// stopped and transcribed as if the user had finished it. Kept apart so the controller stays
/// under the 500-line limit.
/// </summary>
public sealed partial class DictationController
{
    /// <summary>A cancelled recording must be longer than this to be kept (Wispr Flow's figure).</summary>
    public static readonly TimeSpan KeepCancelledAbove = TimeSpan.FromSeconds(3);

    /// <summary>How long a cancelled recording's audio stays in memory for recovery before it is
    /// dropped. Short on purpose: it is spoken audio the user chose to throw away.</summary>
    public static readonly TimeSpan CancelledRecoveryWindow = TimeSpan.FromMinutes(10);

    /// <summary>The longest recording (Wispr Flow's figure). A forgotten hands-free session would
    /// otherwise grow without bound in memory and end as one enormous upload.</summary>
    public static readonly TimeSpan RecordingLimit = TimeSpan.FromMinutes(20);

    /// <summary>How long before <see cref="RecordingLimit"/> the pill warns.</summary>
    public static readonly TimeSpan RecordingLimitWarning = TimeSpan.FromMinutes(1);

    /// <summary>The Overlay is showing the "under a minute left" notice over a recording that
    /// carries on: Finish, a tap and the held key's release still act on it.</summary>
    private bool IsListeningView =>
        View.Phase == OverlayPhase.Listening
        || (View.Phase == OverlayPhase.Notice && View.Notice == OverlayNotice.RecordingLimitSoon);

    private OverlayView ListeningView(ActiveDictation dictation) => new()
    {
        Phase = OverlayPhase.Listening,
        Controls = OverlayControls.Timer | OverlayControls.Cancel | OverlayControls.Finish,
        ListeningSince = dictation.StartedAt,
        Rewrite = dictation.IsRewrite,
        Locked = dictation.IsLocked,
    };

    // ---- Undo and recovery of a cancelled recording -------------------------------------------

    /// <summary>
    /// "Undo" on the Cancelled notice: transcribes the kept recording and pastes it like any
    /// Dictation. Acts only on the notice the Overlay is showing.
    /// </summary>
    public async Task Undo(Guid id)
    {
        // The recorder may still be finishing the cancelled clip; the kept entry exists after that.
        if (_discarding is { IsCompleted: false } pending)
        {
            await pending.ConfigureAwait(true);
        }

        if (View is not { Phase: OverlayPhase.Notice, Notice: OverlayNotice.CancelledUndo } || View.FailedId != id
            || BeginRetry(id, offline: false) is not ({ } failed, { } client))
        {
            return;
        }

        await RunOverlayRetryAsync(failed, client).ConfigureAwait(true);
    }

    /// <summary>Drops every kept cancelled recording: Keep Dictations was turned off, and nothing
    /// is kept then (ADR-0004). A recovery in progress is left to finish.</summary>
    public void DropCancelledRecordings()
    {
        foreach (FailedDictation cancelled in _failed.Items.Where(f => f.Reason == FailureReason.Cancelled && !f.IsRetrying))
        {
            RemoveFailed(cancelled);
        }
    }

    private bool ShouldKeepCancelled(ActiveDictation dictation, TimeSpan recorded)
    {
        // A rewrite's audio is an instruction, and a Retry's audio is already kept.
        if (dictation.IsRewrite || dictation.RetryOf is not null || recorded <= KeepCancelledAbove)
        {
            return false;
        }

        try
        {
            return _collaborators.KeepCancelledRecordings();
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("KeepCancelledRecordings", ex);
            return false;
        }
    }

    /// <summary>Stops the recorder like a discard does, but keeps the clip it returns.</summary>
    private async Task KeepCancelledRecordingAsync(ActiveDictation dictation, Guid id)
    {
        try
        {
            AudioClip clip = await _collaborators.Recorder.StopAsync().ConfigureAwait(true);

            // A clip with no speech possible in it is not worth a recovery entry.
            if (!clip.IsLikelySilent() && !clip.HasNoSignal())
            {
                AddCancelled(dictation, id, clip.WavBytes, clip.Duration);
            }
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("KeepCancelledRecording", ex);
        }
    }

    private void AddCancelled(ActiveDictation dictation, Guid id, byte[] audio, TimeSpan length)
    {
        var cancelled = new FailedDictation(id, audio, length, FailureReason.Cancelled, _collaborators.Timer.UtcNow, dictation.AppName);
        cancelled.Expiry = _collaborators.Timer.Schedule(CancelledRecoveryWindow, () => ExpireCancelled(id));
        _failed.Add(cancelled);
    }

    /// <summary>The recovery window is over: the audio goes, unless a recovery is running (then
    /// the window starts again) or a failed recovery has made it an ordinary Failed Dictation.</summary>
    private void ExpireCancelled(Guid id)
    {
        if (_failed.Find(id) is not { Reason: FailureReason.Cancelled } cancelled)
        {
            return;
        }

        if (cancelled.IsRetrying)
        {
            cancelled.Expiry = _collaborators.Timer.Schedule(CancelledRecoveryWindow, () => ExpireCancelled(id));
            return;
        }

        SimpleFileLogger.LogInfo("Dropped a cancelled recording after its recovery window");
        RemoveFailed(cancelled);
    }

    // ---- The recording cap --------------------------------------------------------------------

    private void ScheduleRecordingLimit(ActiveDictation dictation) =>
        dictation.LimitTimers =
        [
            _collaborators.Timer.Schedule(RecordingLimit - RecordingLimitWarning, () => OnRecordingLimitSoon(dictation)),
            _collaborators.Timer.Schedule(RecordingLimit, () => OnRecordingLimit(dictation)),
        ];

    private bool IsRecording(ActiveDictation dictation) =>
        ReferenceEquals(_active, dictation) && dictation.IsListening && !dictation.Cancellation.IsCancellationRequested;

    /// <summary>A minute before the cap: a passing notice over the recording, which carries on.</summary>
    private void OnRecordingLimitSoon(ActiveDictation dictation)
    {
        if (!IsRecording(dictation) || View.Phase != OverlayPhase.Listening)
        {
            return;
        }

        SetView(new OverlayView { Phase = OverlayPhase.Notice, Notice = OverlayNotice.RecordingLimitSoon });
        _pendingReturn = _collaborators.Timer.Schedule(NoticeDuration, () =>
        {
            if (IsRecording(dictation))
            {
                SetView(ListeningView(dictation));
            }
        });
    }

    /// <summary>At the cap the recording ends as if the user had finished it, so its text is
    /// transcribed and pasted (or kept for Retry) and not lost.</summary>
    private void OnRecordingLimit(ActiveDictation dictation)
    {
        if (!IsRecording(dictation))
        {
            return;
        }

        dictation.HitLimit = true;
        _ = StopAsync(dictation);
    }
}
