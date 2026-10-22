using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.EmailRequests;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Rewrite;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Dictation;

/// <summary>The four Sound Cues (CONTEXT: Sound Cues). Whether they are audible is the player's
/// business: the engine asks for every cue and a disabled player stays silent.</summary>
public enum SoundCue
{
    Start,
    Stop,
    Done,
    Failed,
}

public interface ISoundCues
{
    void Play(SoundCue cue);
}

/// <summary>Mute Other Audio. Whether it is switched on is the ducker's business, as with
/// <see cref="ISoundCues"/>.</summary>
public interface IAudioDucker
{
    /// <summary>Lowers other apps' audio; called when recording begins.</summary>
    void Duck();

    /// <summary>Puts other apps' audio back; called when recording ends, however it ends.</summary>
    void Restore();
}

public interface IForegroundApp
{
    /// <summary>A display name for the app in the foreground, or null if it can't be read.</summary>
    string? CurrentAppName();
}

/// <summary>
/// The engine's clock. <see cref="Schedule"/> callbacks must arrive on the same thread as the
/// controller's inputs (the host's UI thread), because the controller is not thread-safe apart
/// from its level stream and <see cref="DictationController.ShouldSwallowEscape"/>.
/// </summary>
public interface IDictationTimer
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Runs <paramref name="callback"/> once after <paramref name="delay"/>; disposing the
    /// returned handle before then cancels it.</summary>
    IDisposable Schedule(TimeSpan delay, Action callback);
}

/// <summary>Everything <see cref="DictationController"/> talks to. A settable bag rather than an
/// eleven-parameter constructor, so a test names only what it cares about.</summary>
public sealed class DictationCollaborators
{
    public required IAudioRecorder Recorder { get; init; }

    /// <summary>Transcribes in the current Mode (the host passes the Adaptive client).</summary>
    public required ITranscriptionClient Transcription { get; init; }

    /// <summary>Transcribes offline only, for "Retry offline". Null makes offline retries
    /// unavailable.</summary>
    public ITranscriptionClient? OfflineTranscription { get; init; }

    /// <summary>Whether the offline model is on disk now; read each time "Retry offline" is
    /// offered or used, so a model downloaded later makes it appear.</summary>
    public Func<bool> IsOfflineModelInstalled { get; init; } = () => true;

    public required ITextInjector Injector { get; init; }

    /// <summary>Applies a spoken instruction to text (rewrite by voice). Null leaves the feature off.</summary>
    public IRewriteClient? Rewriter { get; init; }

    /// <summary>Writes the email for an Email request: a Rewrite key instruction said with nothing
    /// selected in an email app (ticket 57). Null leaves the feature off.</summary>
    public IEmailWriter? EmailWriter { get; init; }

    /// <summary>Reads and re-makes the focused app's selection for a rewrite. Null leaves the feature off.</summary>
    public ISelectionAccess? Selection { get; init; }

    public required IForegroundApp ForegroundApp { get; init; }

    public required ISoundCues SoundCues { get; init; }

    public required IAudioDucker Ducker { get; init; }

    public required IDictationTimer Timer { get; init; }

    public required LastTranscriptionStore LastTranscription { get; init; }

    public required DictationHistoryStore History { get; init; }

    /// <summary>The Press Enter setting, read at each Dictation's end.</summary>
    public Func<bool> PressEnter { get; init; } = () => false;

    /// <summary>The text Smart cleanup replaced in the Dictation that just finished, or null when it
    /// left the text alone. Taken once per Dictation, right after the transcription.</summary>
    public Func<string?> TakeOriginal { get; init; } = () => null;

    /// <summary>True when the current Mode needs a Gemini key and none is set. Read at each stop,
    /// so a key added in Settings takes effect on the next Dictation.</summary>
    public Func<bool> IsApiKeyMissing { get; init; } = () => false;

    /// <summary>Whether a cancelled recording of more than 3 s may be kept for recovery: true
    /// while Keep Dictations is not "none" (ADR-0004). Read at each Cancel. Off unless the host
    /// says otherwise, so nothing is kept by accident.</summary>
    public Func<bool> KeepCancelledRecordings { get; init; } = () => false;

    /// <summary>The Paste Again key spelled for a notice ("Ctrl+Shift+V"), read when "Paste failed"
    /// is shown so a change in Settings applies at once; null when there is none.</summary>
    public Func<string?> PasteAgainKeyLabel { get; init; } = () => null;
}
