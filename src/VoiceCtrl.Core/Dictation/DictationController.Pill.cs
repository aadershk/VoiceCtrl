using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Dictation;

/// <summary>
/// The pill's own actions and the notices that say why nothing happened (VED-75): Finish while
/// Listening, a press while Transcribing, "Taking longer than usual", a microphone that stops
/// mid-dictation, and the words of a paste that didn't land. Kept apart so the controller stays
/// under the 500-line limit.
/// </summary>
public sealed partial class DictationController
{
    /// <summary>
    /// How long Transcribing runs before the pill says it is taking longer than usual. A Dictation
    /// normally answers in a second or two online; offline Smart cleanup on a 4B model measured
    /// about 6 s on the owner's PC, so 12 s clears the slow-but-normal cases and still speaks up
    /// well before the 30 s request timeout turns the wait into a Failed Dictation.
    /// </summary>
    public static readonly TimeSpan SlowThreshold = TimeSpan.FromSeconds(12);

    /// <summary>The tick beside ✕ while Listening: stops and pastes exactly as releasing the held
    /// key (or tapping it, when hands-free) does. Does nothing unless a Dictation is Listening.</summary>
    public Task Finish() =>
        IsListeningView && _active is { IsListening: true } dictation
            ? StopAsync(dictation)
            : Task.CompletedTask;

    /// <summary>
    /// A key press, or a click on the pill, while a Dictation is Transcribing. The first shows
    /// "Still transcribing" with Cancel and the Transcribing carries on underneath; a press while
    /// that notice (or "Taking longer than usual") is up is the answer to it and cancels, like Esc.
    /// Never starts a Dictation, and does nothing when nothing is Transcribing.
    /// </summary>
    public Task PillPressed()
    {
        if (_active is not { IsListening: false } dictation)
        {
            return Task.CompletedTask;
        }

        if (View.Phase == OverlayPhase.Notice)
        {
            Cancel();
        }
        else if (View.Phase == OverlayPhase.Transcribing)
        {
            SetView(NoticeWithCancel(dictation.IsWriting ? OverlayNotice.StillWriting : OverlayNotice.StillTranscribing));
            _pendingReturn = _collaborators.Timer.Schedule(NoticeDuration, () => ResumeTranscribing(dictation));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The host saw the microphone stop delivering while a Dictation was Listening (unplugged,
    /// disabled, taken over by another app). What was recorded is thrown away, like Cancel, and the
    /// pill says so. Call on the UI thread; the recorder's own event fires on its capture thread.
    /// </summary>
    public void MicrophoneStopped()
    {
        if (!IsListeningView || _active is not { IsListening: true } dictation)
        {
            return;
        }

        _active = null;
        dictation.Cancellation.Cancel();
        dictation.DisposeLimitTimers();
        _collaborators.Ducker.Restore();
        _discarding = DiscardRecordingAsync();
        ShowNotice(OverlayNotice.MicStopped);
    }

    /// <summary>Shows Transcribing and starts the clock for "Taking longer than usual".</summary>
    private void BeginTranscribing(ActiveDictation dictation)
    {
        SetView(TranscribingViewFor(dictation));
        dictation.SlowTimer = _collaborators.Timer.Schedule(SlowThreshold, () => OnTranscribingSlow(dictation));
    }

    private void OnTranscribingSlow(ActiveDictation dictation)
    {
        if (!ReferenceEquals(_active, dictation) || dictation.IsListening || dictation.Cancellation.IsCancellationRequested)
        {
            return;
        }

        dictation.IsSlow = true;

        // Under the "Still transcribing" notice, that notice's own return will show this one.
        if (View.Phase == OverlayPhase.Transcribing)
        {
            SetView(NoticeWithCancel(OverlayNotice.TakingLonger));
        }
    }

    /// <summary>The passing "Still transcribing" notice is over: back to Transcribing, or to the
    /// slow notice when the wait has passed <see cref="SlowThreshold"/> meanwhile.</summary>
    private void ResumeTranscribing(ActiveDictation dictation)
    {
        if (!ReferenceEquals(_active, dictation))
        {
            ReturnToRest();
            return;
        }

        if (dictation.IsSlow)
        {
            SetView(NoticeWithCancel(OverlayNotice.TakingLonger));
        }
        else
        {
            SetView(TranscribingViewFor(dictation));
        }
    }

    private static OverlayView NoticeWithCancel(OverlayNotice notice) =>
        new() { Phase = OverlayPhase.Notice, Notice = notice, Controls = OverlayControls.Cancel };

    /// <summary>The notice for a paste that did not land. "Paste failed" names the Paste Again key
    /// so the way to recover is on the pill; the other two say the text is on the clipboard.</summary>
    private void ShowPasteNotice(InjectionResult result)
    {
        switch (result)
        {
            case InjectionResult.ClipboardOnlyElevatedTarget:
                ShowNotice(OverlayNotice.CopiedPressCtrlV);
                break;

            case InjectionResult.ClipboardOnlyNoTextBox:
                ShowNotice(OverlayNotice.SelectTextboxFirst);
                break;

            default:
                ShowNotice(OverlayNotice.PasteFailed, ReadPasteAgainKey());
                break;
        }
    }

    private string? ReadPasteAgainKey()
    {
        try
        {
            return _collaborators.PasteAgainKeyLabel();
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("PasteAgainKeyLabel", ex);
            return null;
        }
    }
}
