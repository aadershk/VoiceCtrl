namespace VoiceCtrl.Core.Dictation;

/// <summary>
/// A Failed Dictation (CONTEXT) as kept by <see cref="DictationController"/>: its audio lives
/// here, in memory only, until a Retry succeeds, it is dismissed, it is pushed out by newer
/// failures, or VoiceCtrl quits. The audio is never exposed or written to disk.
/// </summary>
public sealed class FailedDictation
{
    internal FailedDictation(Guid id, byte[] audio, TimeSpan audioLength, FailureReason reason, DateTimeOffset failedAtUtc, string? appName)
    {
        Id = id;
        Audio = audio;
        AudioLength = audioLength;
        Reason = reason;
        FailedAtUtc = failedAtUtc;
        AppName = appName;
    }

    public Guid Id { get; }

    public FailureReason Reason { get; internal set; }

    public DateTimeOffset FailedAtUtc { get; }

    /// <summary>The app the Dictation was meant for, for the Dictations screen's "app · time".</summary>
    public string? AppName { get; }

    /// <summary>How long the kept recording is ("12 s of audio" on the Dictations screen).</summary>
    public TimeSpan AudioLength { get; }

    /// <summary>True while a Retry of this one is running, so a second Retry is ignored.</summary>
    public bool IsRetrying { get; internal set; }

    internal byte[] Audio { get; }

    /// <summary>The clock that drops a cancelled recording's audio after its recovery window.</summary>
    internal IDisposable? Expiry { get; set; }
}

/// <summary>How a Retry from the Dictations screen went, for the page to say so.</summary>
public enum RetryOutcome
{
    /// <summary>Nothing ran: the failure is gone, already retrying, or offline was asked for with
    /// no offline model.</summary>
    Ignored,

    /// <summary>The Transcription is now the latest Dictation.</summary>
    Transcribed,

    /// <summary>The audio held no speech; the failure is dropped.</summary>
    NoSpeech,

    /// <summary>It failed again; the audio is still kept, with the new reason.</summary>
    Failed,
}
