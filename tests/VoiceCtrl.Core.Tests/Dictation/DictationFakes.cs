using System.Net.Http;
using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.EmailRequests;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Rewrite;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Tests.Dictation;

internal sealed class FakeRecorder : IAudioRecorder
{
    public event Action<float>? LevelChanged;

    public bool IsRecording { get; private set; }

    public int Starts { get; private set; }

    public Exception? StartThrows { get; set; }

    /// <summary>Length of the clip StopAsync returns; one second is comfortably "not silent".</summary>
    public TimeSpan ClipLength { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The WAV bytes StopAsync returns; null makes a one-byte stand-in.</summary>
    public byte[]? ClipBytes { get; set; }

    public void Start()
    {
        if (StartThrows is not null)
        {
            throw StartThrows;
        }

        // Like the real recorder: a start while it still reports recording does nothing.
        if (IsRecording)
        {
            return;
        }

        Starts++;
        IsRecording = true;
    }

    public Exception? StopThrows { get; set; }

    /// <summary>When set, StopAsync keeps reporting "recording" until the test completes it, like
    /// the real recorder between StopRecording and its last flushed buffer.</summary>
    public TaskCompletionSource? StopGate { get; set; }

    public async Task<AudioClip> StopAsync()
    {
        if (StopGate is not null)
        {
            await StopGate.Task;
        }

        IsRecording = false;
        if (StopThrows is not null)
        {
            throw StopThrows;
        }

        return new AudioClip(ClipBytes ?? [(byte)Starts], ClipLength);
    }

    public void RaiseLevel(float level) => LevelChanged?.Invoke(level);
}

/// <summary>Answers each transcription from a queue of scripted outcomes, or holds it open until
/// the test calls <see cref="Complete"/> when <see cref="Hold"/> is set.</summary>
internal sealed class FakeTranscriber : ITranscriptionClient
{
    private readonly Queue<Func<string?>> _scripted = new();
    private TaskCompletionSource<string?>? _held;

    public bool Hold { get; set; }

    public List<byte[]> Calls { get; } = [];

    public CancellationToken LastToken { get; private set; }

    public void Returns(string? text) => _scripted.Enqueue(() => text);

    public void Throws(Exception ex) => _scripted.Enqueue(() => throw ex);

    public void Complete(string? text) => _held!.SetResult(text);

    public Task<string?> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        Calls.Add(wavBytes);
        LastToken = cancellationToken;
        if (Hold)
        {
            _held = new TaskCompletionSource<string?>();
            cancellationToken.Register(() => _held.TrySetCanceled(cancellationToken));
            return _held.Task;
        }

        try
        {
            return Task.FromResult(_scripted.Count > 0 ? _scripted.Dequeue()() : "hello world");
        }
        catch (Exception ex)
        {
            return Task.FromException<string?>(ex);
        }
    }

    public void Dispose()
    {
    }
}

/// <summary>Answers each rewrite with <see cref="Outcome"/> (or throws <see cref="Throws"/>), and
/// notes what it was asked.</summary>
internal sealed class FakeRewriter : IRewriteClient
{
    public RewriteOutcome Outcome { get; set; } = RewriteOutcome.Rewritten("REWRITTEN");

    public Exception? Throws { get; set; }

    public List<(byte[] Audio, string Source)> Calls { get; } = [];

    /// <summary>What <see cref="HearAsync"/> writes down for the instruction.</summary>
    public string? Heard { get; set; } = "make it shorter";

    public int Hearings { get; private set; }

    /// <summary>The words each rewrite was handed from an earlier hearing, if any.</summary>
    public List<string?> HeardPassed { get; } = [];

    public int Prewarms { get; private set; }

    public Task<RewriteOutcome> RewriteAsync(byte[] instructionAudio, string source, CancellationToken cancellationToken, string? heard = null)
    {
        Calls.Add((instructionAudio, source));
        HeardPassed.Add(heard);
        return Throws is null ? Task.FromResult(Outcome) : Task.FromException<RewriteOutcome>(Throws);
    }

    public Task<string?> HearAsync(byte[] instructionAudio, CancellationToken cancellationToken)
    {
        Hearings++;
        return Throws is null ? Task.FromResult(Heard) : Task.FromException<string?>(Throws);
    }

    public Task PrewarmAsync(CancellationToken cancellationToken = default)
    {
        Prewarms++;
        return Task.CompletedTask;
    }
}

/// <summary>An email writer that applies when <see cref="InAnEmailApp"/> is set, takes
/// <see cref="RequestWords"/> for an Email request and writes <see cref="Email"/>; with
/// <see cref="Hold"/>, the write waits for <see cref="Complete"/>.</summary>
internal sealed class FakeEmailWriter : IEmailWriter
{
    private TaskCompletionSource<string?>? _held;

    public bool InAnEmailApp { get; set; }

    public string? RequestWords { get; set; }

    public string? Email { get; set; } = "Dear Mark,\n\nThank you for your time.\n\nKind regards,\nAlex";

    public Exception? Throws { get; set; }

    public bool Hold { get; set; }

    public List<EmailRequest> Writes { get; } = [];

    public int Prewarms { get; private set; }

    public bool AppliesHere() => InAnEmailApp;

    public EmailRequest? ForInstruction(string instruction) =>
        InAnEmailApp && instruction == RequestWords ? new EmailRequest(instruction, "Mark") : null;

    public Task<string?> WriteAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        Writes.Add(request);
        if (Throws is not null)
        {
            return Task.FromException<string?>(Throws);
        }

        if (!Hold)
        {
            return Task.FromResult(Email);
        }

        _held = new TaskCompletionSource<string?>();
        cancellationToken.Register(() => _held.TrySetCanceled(cancellationToken));
        return _held.Task;
    }

    public void Complete(string? email) => _held!.SetResult(email);

    public Task PrewarmAsync(CancellationToken cancellationToken = default)
    {
        Prewarms++;
        return Task.CompletedTask;
    }
}

/// <summary>A focused app with <see cref="Selected"/> selected, or nothing when it is null.</summary>
internal sealed class FakeSelection : ISelectionAccess
{
    public string? Selected { get; set; }

    public bool SelectPrecedingResult { get; set; } = true;

    /// <summary>What was asked, in order: "copy", then "select:&lt;text&gt;" for each preceding-text selection.</summary>
    public List<string> Asked { get; } = [];

    public Task<string?> CopySelectionAsync(CancellationToken cancellationToken)
    {
        Asked.Add("copy");
        return Task.FromResult(Selected);
    }

    public Task<bool> SelectPrecedingAsync(string text, CancellationToken cancellationToken)
    {
        Asked.Add($"select:{text}");
        return Task.FromResult(SelectPrecedingResult);
    }
}

internal sealed class FakeInjector : ITextInjector
{
    public List<string> Pasted { get; } = [];

    public InjectionResult Result { get; set; } = InjectionResult.Injected;

    /// <summary>Everything sent, in order: each paste's text, and "⏎" for each Enter.</summary>
    public List<string> Sent { get; } = [];

    public int Enters { get; private set; }

    public void PressEnter()
    {
        Enters++;
        Sent.Add("⏎");
    }

    public Task<InjectionResult> InjectAsync(string text)
    {
        Pasted.Add(text);
        Sent.Add(text);
        return Task.FromResult(Result);
    }
}

internal sealed class FakeSoundCues : ISoundCues
{
    public List<SoundCue> Played { get; } = [];

    public void Play(SoundCue cue) => Played.Add(cue);
}

internal sealed class FakeDucker : IAudioDucker
{
    public bool IsDucked { get; private set; }

    public void Duck() => IsDucked = true;

    public void Restore() => IsDucked = false;
}

internal sealed class FakeForegroundApp : IForegroundApp
{
    public string? Name { get; set; } = "Notepad";

    public string? CurrentAppName() => Name;
}

/// <summary>A manual clock: time moves only when the test calls <see cref="Advance"/>, and due
/// callbacks run synchronously inside it.</summary>
internal sealed class FakeTimer : IDictationTimer
{
    private readonly List<(DateTimeOffset Due, Action Callback, Handle Handle)> _pending = [];

    public DateTimeOffset UtcNow { get; private set; } = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var handle = new Handle();
        _pending.Add((UtcNow + delay, callback, handle));
        return handle;
    }

    public void Advance(TimeSpan by)
    {
        UtcNow += by;
        foreach (var entry in _pending.Where(p => p.Due <= UtcNow).ToList())
        {
            _pending.Remove(entry);
            if (!entry.Handle.Disposed)
            {
                entry.Callback();
            }
        }
    }

    public void AdvanceMs(double milliseconds) => Advance(TimeSpan.FromMilliseconds(milliseconds));

    private sealed class Handle : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}

/// <summary>A 16 kHz mono 16-bit WAV (44-byte header) of digital silence.</summary>
internal static class WavBytes
{
    public static byte[] Silent(TimeSpan length) => new byte[44 + 2 * (int)(16000 * length.TotalSeconds)];
}

/// <summary>One controller wired to a full set of fakes, plus the failures each reason maps from.</summary>
internal sealed class Rig
{
    public FakeRecorder Recorder { get; } = new();
    public FakeTranscriber Transcriber { get; } = new();
    public FakeTranscriber OfflineTranscriber { get; } = new();
    public FakeInjector Injector { get; } = new();
    public FakeRewriter Rewriter { get; } = new();
    public FakeEmailWriter EmailWriter { get; } = new();
    public FakeSelection Selection { get; } = new();
    public FakeSoundCues Cues { get; } = new();
    public FakeDucker Ducker { get; } = new();
    public FakeForegroundApp ForegroundApp { get; } = new();
    public FakeTimer Timer { get; } = new();
    public LastTranscriptionStore Last { get; } = new();
    public DictationHistoryStore History { get; } = DictationHistoryStore.ForTesting();
    public bool ApiKeyMissing { get; set; }
    public bool OfflineModelInstalled { get; set; } = true;
    public bool PressEnter { get; set; }

    /// <summary>Whether a cancelled recording may be kept (Keep Dictations is not "none").</summary>
    public bool KeepCancelled { get; set; } = true;

    /// <summary>The Paste Again key as the settings spell it; null for none.</summary>
    public string? PasteAgainKey { get; set; } = "Ctrl+Shift+V";

    /// <summary>What Smart cleanup would report having replaced; taken by the next Dictation.</summary>
    public string? Original { get; set; }
    public List<OverlayView> Views { get; } = [];
    public List<float> Levels { get; } = [];
    public DictationController Controller { get; }

    public Rig(bool offlineInstalled = true, bool rewriteAvailable = true)
    {
        Controller = new DictationController(new DictationCollaborators
        {
            Recorder = Recorder,
            Transcription = Transcriber,
            OfflineTranscription = offlineInstalled ? OfflineTranscriber : null,
            Injector = Injector,
            Rewriter = rewriteAvailable ? Rewriter : null,
            EmailWriter = EmailWriter,
            Selection = rewriteAvailable ? Selection : null,
            ForegroundApp = ForegroundApp,
            SoundCues = Cues,
            Ducker = Ducker,
            Timer = Timer,
            LastTranscription = Last,
            History = History,
            IsApiKeyMissing = () => ApiKeyMissing,
            IsOfflineModelInstalled = () => OfflineModelInstalled,
            PressEnter = () => PressEnter,
            KeepCancelledRecordings = () => KeepCancelled,
            PasteAgainKeyLabel = () => PasteAgainKey,
            TakeOriginal = () =>
            {
                string? original = Original;
                Original = null;
                return original;
            },
        });
        Controller.ViewChanged += Views.Add;
        Controller.LevelChanged += Levels.Add;
    }

    public OverlayPhase Phase => Controller.View.Phase;

    public static Exception NoInternet() => new HttpRequestException("offline");

    public static Exception TimedOut() => new TaskCanceledException("timeout");

    public static Exception ServiceError() => new TranscriptionException("500");

    /// <summary>Drives one Dictation that fails with <paramref name="failure"/>, started from the
    /// Ready tile, and returns the kept Failed Dictation's id.</summary>
    public async Task<Guid> FailOneAsync(Exception? failure = null)
    {
        if (Phase == OverlayPhase.Hidden)
        {
            await Controller.ToggleBar();
        }

        Transcriber.Throws(failure ?? NoInternet());
        await Controller.Tap();
        await Controller.Tap();
        return Controller.FailedDictations[0].Id;
    }
}
