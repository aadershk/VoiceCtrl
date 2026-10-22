namespace VoiceCtrl.Core.Dictation;

/// <summary>The Overlay's states from the Overlay design (research 11, "States").</summary>
public enum OverlayPhase
{
    Hidden,

    /// <summary>"Always show the Overlay" is on and nothing is happening: the grey resting handle,
    /// which turns into the Ready tile under the pointer (DESIGN §9). Otherwise the same as Hidden.</summary>
    Handle,
    Ready,
    Listening,
    Transcribing,
    Pasted,
    NoSpeech,
    Failed,
    Notice,
}

/// <summary>The controls the page may offer in the current phase. Which of them appear only on
/// hover is the page's layout decision (DESIGN §9), not the engine's.</summary>
[Flags]
public enum OverlayControls
{
    None = 0,

    /// <summary>The Ready tile itself: click to dictate.</summary>
    Start = 1,
    Timer = 2,
    Cancel = 4,
    Retry = 8,
    Dismiss = 16,

    /// <summary>The tick beside ✕ while Listening: stop and paste, as releasing the key would.</summary>
    Finish = 32,

    /// <summary>"Undo" on the Cancelled notice: transcribe the cancelled recording after all.</summary>
    Undo = 64,
}

/// <summary>Why a Failed Dictation failed. The page owns the words ("No internet", "Timed out",
/// "Service error").</summary>
public enum FailureReason
{
    NoInternet,
    TimedOut,
    ServiceError,

    /// <summary>Not a failure: the recording was cancelled after more than 3 s and kept, so its
    /// text can still be recovered (ADR-0004). Becomes a real reason if a recovery attempt fails.</summary>
    Cancelled,
}

/// <summary>The Overlay's short "icon + few words" messages. The page owns the words.</summary>
public enum OverlayNotice
{
    MicUnavailable,
    AddApiKey,
    CopiedPressCtrlV,
    PasteFailed,

    /// <summary>Copying the latest Transcription (the tray's "Copy latest") didn't reach the
    /// clipboard: "Copy failed · Try again" (DESIGN §12).</summary>
    CopyFailed,

    /// <summary>A rewrite by voice found nothing to rewrite: no selection, and the last Dictation
    /// is not behind the caret.</summary>
    NothingToRewrite,

    /// <summary>Offline, and the local language model a rewrite needs is not installed.</summary>
    RewriteNeedsLanguageModel,

    /// <summary>The rewrite could not be made (too slow, a service error, an unusable reply); the
    /// text is as it was.</summary>
    RewriteFailed,

    /// <summary>An Email request's email could not be written (too slow, a service error, or a body
    /// that added what the request did not say); nothing was pasted (ticket 57).</summary>
    EmailNotWritten,

    /// <summary>The text is on the clipboard but nothing that takes text has the focus: "Select a
    /// textbox first" (VED-75).</summary>
    SelectTextboxFirst,

    /// <summary>The recording was digital silence: the microphone gave no audio at all (muted or
    /// dead), as opposed to audio with no words in it.</summary>
    NoMicAudio,

    /// <summary>There was audio, but the service found no words in it.</summary>
    NoWordsHeard,

    /// <summary>The microphone stopped delivering mid-dictation (unplugged, disabled, taken over);
    /// the recording was thrown away.</summary>
    MicStopped,

    /// <summary>A key or the pill was pressed while Transcribing; offers Cancel, and the
    /// Transcribing continues underneath.</summary>
    StillTranscribing,

    /// <summary>A key or the pill was pressed while an Email request's email is being written; like
    /// <see cref="StillTranscribing"/>, which it stands in for then (ticket 57).</summary>
    StillWriting,

    /// <summary>Transcribing has run past <see cref="DictationController.SlowThreshold"/>; stays,
    /// with Cancel, until the Transcribing ends.</summary>
    TakingLonger,

    /// <summary>A recording over 3 s was cancelled and kept; offers Undo (ADR-0004).</summary>
    CancelledUndo,

    /// <summary>The recording is a minute from the cap; passes, and Listening carries on.</summary>
    RecordingLimitSoon,

    /// <summary>The recording was stopped at the cap and its text pasted.</summary>
    RecordingLimitReached,
}

/// <summary>
/// Everything the Overlay page renders, as one immutable value. The controller raises a new one
/// on every change, so the page is a pure function of the latest view.
/// </summary>
public sealed record OverlayView
{
    public static readonly OverlayView Hidden = new() { Phase = OverlayPhase.Hidden };

    /// <summary>The resting handle; its Ready tile is a Start control.</summary>
    public static readonly OverlayView Handle = new() { Phase = OverlayPhase.Handle, Controls = OverlayControls.Start };

    public required OverlayPhase Phase { get; init; }

    public OverlayControls Controls { get; init; }

    /// <summary>When recording began, for the hover timer. Set only while Listening.</summary>
    public DateTimeOffset? ListeningSince { get; init; }

    /// <summary>Set only in <see cref="OverlayPhase.Failed"/>.</summary>
    public FailureReason? FailureReason { get; init; }

    /// <summary>The kept Failed Dictation the notice is about, for Retry and Dismiss.</summary>
    public Guid? FailedId { get; init; }

    /// <summary>Set only in <see cref="OverlayPhase.Notice"/>.</summary>
    public OverlayNotice? Notice { get; init; }

    /// <summary>The Paste Again key as the settings spell it ("Ctrl+Shift+V"), set on the Paste
    /// failed notice so it can name the way to recover; null when there is none.</summary>
    public string? NoticeKey { get; init; }

    /// <summary>True while the Listening or Transcribing is for a rewrite by voice, not a Dictation.</summary>
    public bool Rewrite { get; init; }

    /// <summary>True while Transcribing is VoiceCtrl writing an email from a Rewrite key request
    /// (ticket 57), which takes seconds longer than a rewrite, so the pill says so.</summary>
    public bool Writing { get; init; }

    /// <summary>True while a Dictation is locked on hands-free (VED-74): the page shows the
    /// Listening look plus a lock cue.</summary>
    public bool Locked { get; init; }

    /// <summary>True when this view comes from a Cancel, so the page plays the "thrown away" exit.</summary>
    public bool AfterCancel { get; init; }
}
