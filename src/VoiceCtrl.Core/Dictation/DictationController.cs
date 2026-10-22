using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Dictation;

/// <summary>
/// The Dictation engine (spec §5.1): turns Hotkey, Trigger Key and Overlay gestures into
/// Dictations, and reports what the Overlay should show as one <see cref="OverlayView"/> per
/// change. Every Dictation rule lives here, UI-free, so the Overlay page only renders.
///
/// Threading: inputs, timer callbacks and awaited continuations all run on one thread (the host's
/// UI thread; awaits keep its context). The exceptions are the level stream, raised on the
/// recorder's capture thread, and <see cref="ShouldSwallowEscape"/>, read from the input hook's
/// thread. Input methods return a Task that completes when the work they started is finished;
/// it never faults, so the host may discard it.
/// </summary>
public sealed partial class DictationController
{
    // How long each passing state stays up (research 11, States). Notices have no designed figure;
    // 2.5 s gives "Copied · press Ctrl+V" time to be read and acted on.
    internal static readonly TimeSpan PastedDuration = TimeSpan.FromMilliseconds(900);
    internal static readonly TimeSpan NoSpeechDuration = TimeSpan.FromMilliseconds(1800);
    internal static readonly TimeSpan NoticeDuration = TimeSpan.FromMilliseconds(2500);
    internal static readonly TimeSpan FailedDuration = TimeSpan.FromSeconds(8);

    /// <summary>A hold shorter than this is a tap and does nothing, silently (VED-74). Also how
    /// soon after a tap's key-down a second press must come to lock the recording on.</summary>
    internal static readonly TimeSpan TapThreshold = TimeSpan.FromMilliseconds(500);

    /// <summary>The level stream's cap: 60 Hz.</summary>
    private static readonly TimeSpan MinLevelInterval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private readonly DictationCollaborators _collaborators;
    private readonly FailedDictationList _failed = new();

    /// <summary>Where the Overlay rests when no Dictation or message is on it: Hidden (the handle
    /// when <see cref="AlwaysShow"/> is on), or the Ready tile after a Hotkey show. A Dictation
    /// ends by returning here.</summary>
    private OverlayPhase _rest = OverlayPhase.Hidden;

    private bool _alwaysShow;

    /// <summary>The Dictation (or Overlay Retry) currently Listening or Transcribing.</summary>
    private ActiveDictation? _active;

    /// <summary>The scheduled return to rest after a passing outcome or message.</summary>
    private IDisposable? _pendingReturn;

    private volatile bool _swallowEscape;

    // Read on the capture thread by the level stream.
    private volatile bool _isListening;
    private long _lastLevelTicks;
    private Task? _discarding;

    /// <summary>When the last quick tap's press began, so a second press within
    /// <see cref="TapThreshold"/> of it locks the next recording on. Null once used or stale.</summary>
    private DateTimeOffset? _lastTapStartedAt;

    public DictationController(DictationCollaborators collaborators)
    {
        _collaborators = collaborators;
        _failed.Changed += () => FailedDictationsChanged?.Invoke();
        _collaborators.Recorder.LevelChanged += OnRecorderLevel;
    }

    public OverlayView View { get; private set; } = OverlayView.Hidden;

    public event Action<OverlayView>? ViewChanged;

    /// <summary>Mic level, 0..1, while Listening, at most 60 times a second. Raised on the
    /// recorder's capture thread; the host forwards it to the page.</summary>
    public event Action<float>? LevelChanged;

    /// <summary>Raised when a Failed Dictation is kept, retried, updated or removed; the
    /// Dictations screen re-reads <see cref="FailedDictations"/>.</summary>
    public event Action? FailedDictationsChanged;

    /// <summary>Read by the host's keyboard hook on its own thread: swallow Esc only while a
    /// Dictation is Listening or Transcribing, so an Esc that cancels never reaches the app.</summary>
    public bool ShouldSwallowEscape => _swallowEscape;

    /// <summary>
    /// "Always show the Overlay" (DESIGN §9): rest on the grey handle instead of hiding. A change
    /// applies at once when the Overlay is resting hidden or on the handle; a Dictation or message
    /// on screen is left alone and ends on the new resting state.
    /// </summary>
    public bool AlwaysShow
    {
        get => _alwaysShow;
        set
        {
            _alwaysShow = value;
            if (View.Phase is OverlayPhase.Hidden or OverlayPhase.Handle)
            {
                ReturnToRest();
            }
        }
    }

    /// <summary>The kept Failed Dictations, newest first (the Overlay shows the newest; the
    /// Dictations screen shows each one).</summary>
    public IReadOnlyList<FailedDictation> FailedDictations => _failed.Items;

    /// <summary>
    /// Hotkey double-tap: shows the Ready tile when the Overlay is hidden or on its handle, and
    /// otherwise means "hide". A Dictation in progress is not thrown away by that: Listening stops
    /// and pastes, Transcribing finishes, and either then ends hidden (or on the handle).
    /// </summary>
    public Task ToggleBar()
    {
        if (View.Phase is OverlayPhase.Hidden or OverlayPhase.Handle)
        {
            _rest = OverlayPhase.Ready;
            ReturnToRest();
            return Task.CompletedTask;
        }

        _rest = OverlayPhase.Hidden;
        switch (View.Phase)
        {
            case OverlayPhase.Listening:
                return StopAsync(_active!);

            case OverlayPhase.Transcribing:
                return Task.CompletedTask;

            case OverlayPhase.Notice when IsListeningView:
                return StopAsync(_active!);

            // The "Still transcribing" and "Taking longer" notices sit over a running transcription.
            case OverlayPhase.Notice when _active is not null:
                return Task.CompletedTask;

            default:
                ReturnToRest();
                return Task.CompletedTask;
        }
    }

    /// <summary>Trigger Key tap: starts a Dictation when the Overlay is showing and idle, stops one
    /// that is Listening. The resting handle counts as hidden here, so a stray tap can't start
    /// recording just because "Always show" is on.</summary>
    public Task Tap()
    {
        if (_active is { IsListening: false })
        {
            return PillPressed();
        }

        if (IsListeningView && _active is { IsListening: true } recording)
        {
            return StopAsync(recording);
        }

        switch (View.Phase)
        {
            case OverlayPhase.Hidden:
            case OverlayPhase.Handle:
            case OverlayPhase.Transcribing:
                return Task.CompletedTask;

            case OverlayPhase.Listening:
                return StopAsync(_active!);

            default:
                return StartDictation(startedByHold: false);
        }
    }

    /// <summary>A click on the Ready tile, whether it came up with the Hotkey or out of the resting
    /// handle: starts a Dictation, which ends back where the Overlay was resting.</summary>
    public Task StartFromOverlay()
    {
        return View.Controls.HasFlag(OverlayControls.Start)
            ? StartDictation(startedByHold: false)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Esc or the Overlay's ✕ during a Dictation: nothing is transcribed or pasted. A recording of
    /// more than 3 s is kept in memory for a while, with an Undo notice, when Keep Dictations is
    /// on (CONTEXT: Cancel, ADR-0004). Does nothing when no Dictation is in progress.
    /// </summary>
    public void Cancel() => Discard(afterCancel: true);

    /// <summary>Drops the Dictation in progress. A Cancel plays the "thrown away" exit; a tap's
    /// discard is not a gesture the user made on purpose, so it just leaves.</summary>
    private void Discard(bool afterCancel)
    {
        if (_active is not { } dictation)
        {
            return;
        }

        _active = null;
        dictation.Cancellation.Cancel();
        dictation.SlowTimer?.Dispose();
        dictation.DisposeLimitTimers();
        Guid? kept = null;
        if (dictation.IsListening)
        {
            _collaborators.Ducker.Restore();
            if (afterCancel && ShouldKeepCancelled(dictation, _collaborators.Timer.UtcNow - dictation.StartedAt))
            {
                kept = Guid.NewGuid();
                _discarding = KeepCancelledRecordingAsync(dictation, kept.Value);
            }
            else
            {
                _discarding = DiscardRecordingAsync();
            }
        }
        else if (afterCancel && dictation.Audio is { } audio && ShouldKeepCancelled(dictation, dictation.AudioLength))
        {
            // Cancelled while Transcribing: the clip is in hand, so it is kept like a listening one.
            kept = Guid.NewGuid();
            AddCancelled(dictation, kept.Value, audio, dictation.AudioLength);
        }

        // Cancelling a Retry stops the retry only: its audio was already kept, and dropping it on
        // Esc would lose the Dictation for good. It stays on the Dictations screen.
        if (dictation.RetryOf is { } failed)
        {
            failed.IsRetrying = false;
            _failed.RaiseChanged();
        }

        if (kept is { } keptId)
        {
            ShowForAWhile(
                new OverlayView { Phase = OverlayPhase.Notice, Notice = OverlayNotice.CancelledUndo, Controls = OverlayControls.Undo, FailedId = keptId },
                FailedDuration);
            return;
        }

        ReturnToRest(afterCancel);
    }

    /// <summary>
    /// Starts recording. Esc stops the old recording in the background, and the recorder still
    /// reports "recording" until that stop completes, so a start in that window would silently do
    /// nothing and then be stopped by the pending discard (VED-49: Esc, then a hold within tens of
    /// milliseconds ended in Mic unavailable). A start that lands then waits for the discard.
    /// </summary>
    private Task StartDictation(bool startedByHold, bool isRewrite = false, bool isLocked = false)
    {
        if (_discarding is { IsCompleted: false } pending)
        {
            return StartAfterAsync(pending, startedByHold, isRewrite, isLocked);
        }

        StartNow(startedByHold, isRewrite, isLocked);
        return Task.CompletedTask;
    }

    private async Task StartAfterAsync(Task pendingDiscard, bool startedByHold, bool isRewrite, bool isLocked)
    {
        await pendingDiscard.ConfigureAwait(true);

        // Whatever else the user did meanwhile may have started or begun a Dictation already.
        if (View.Phase is not (OverlayPhase.Listening or OverlayPhase.Transcribing))
        {
            StartNow(startedByHold, isRewrite, isLocked);
        }
    }

    private void StartNow(bool startedByHold, bool isRewrite, bool isLocked)
    {
        try
        {
            // First, before any Overlay work: the user is already speaking, and every millisecond
            // before the device is live is clipped off the front of the Dictation.
            _collaborators.Recorder.Start();
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("StartRecording", ex);
            ShowNotice(OverlayNotice.MicUnavailable);
            return;
        }

        _collaborators.Ducker.Duck();
        _collaborators.SoundCues.Play(SoundCue.Start);
        _ = isRewrite ? PrewarmRewriteAsync() : _collaborators.Transcription.PrewarmConnectionAsync();

        var dictation = new ActiveDictation(
            _collaborators.Timer.UtcNow, startedByHold, _collaborators.ForegroundApp.CurrentAppName()) { IsRewrite = isRewrite, IsLocked = isLocked };
        _active = dictation;
        SetView(ListeningView(dictation));
        ScheduleRecordingLimit(dictation);
    }

    /// <summary>Ends recording and hands the clip on. Never throws: an unforeseen failure is
    /// logged and the Overlay returns to rest rather than sticking on Transcribing.</summary>
    private async Task StopAsync(ActiveDictation dictation)
    {
        dictation.IsListening = false;
        dictation.DisposeLimitTimers();
        _collaborators.Ducker.Restore();
        _collaborators.SoundCues.Play(SoundCue.Stop);
        BeginTranscribing(dictation);

        try
        {
            AudioClip clip;
            try
            {
                clip = await _collaborators.Recorder.StopAsync().ConfigureAwait(true);
            }
            catch (Exception ex) when (!IsSuperseded(dictation))
            {
                SimpleFileLogger.LogError("StopRecording", ex);
                _active = null;
                ShowNotice(OverlayNotice.MicUnavailable);
                return;
            }

            if (IsSuperseded(dictation))
            {
                return;
            }

            if (clip.IsLikelySilent())
            {
                _active = null;
                ShowForAWhile(new OverlayView { Phase = OverlayPhase.NoSpeech }, NoSpeechDuration);
                return;
            }

            if (clip.HasNoSignal())
            {
                _active = null;
                ShowNotice(OverlayNotice.NoMicAudio);
                return;
            }

            if (_collaborators.IsApiKeyMissing())
            {
                _active = null;
                ShowNotice(OverlayNotice.AddApiKey);
                return;
            }

            // Held so a Cancel while Transcribing can still keep the recording (ADR-0004).
            dictation.Audio = clip.WavBytes;
            dictation.AudioLength = clip.Duration;

            await (dictation.IsRewrite
                ? RewriteAndPasteAsync(dictation, clip.WavBytes)
                : TranscribeAndPasteAsync(dictation, clip.WavBytes, clip.Duration, _collaborators.Transcription)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("StopDictation", ex);
            if (ReferenceEquals(_active, dictation))
            {
                _active = null;
                ReturnToRest();
            }
        }
    }

    /// <summary>The shared tail of a Dictation and an Overlay Retry: transcribe, record, paste,
    /// and show how it went.</summary>
    private async Task TranscribeAndPasteAsync(ActiveDictation dictation, byte[] audio, TimeSpan audioLength, ITranscriptionClient client)
    {
        string? text;
        try
        {
            text = await client.TranscribeAsync(audio, dictation.Cancellation.Token).ConfigureAwait(true);
        }
        catch (Exception) when (IsSuperseded(dictation))
        {
            return;
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("Transcription", ex);
            _active = null;
            ShowFailed(dictation, audio, audioLength, Classify(ex));
            return;
        }

        if (IsSuperseded(dictation))
        {
            return;
        }

        _active = null;
        if (dictation.RetryOf is { } retried)
        {
            retried.IsRetrying = false;
            RemoveFailed(retried);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            ShowNotice(OverlayNotice.NoWordsHeard);
            return;
        }

        // Taken before anything else can start a new transcription and clear it.
        string? original = _collaborators.TakeOriginal();

        // Read at each Dictation, so the Settings switch applies to the next one.
        PressEnterResult spoken = _collaborators.PressEnter()
            ? PressEnterDetector.Detect(text)
            : new PressEnterResult(text, PressEnter: false);
        text = spoken.Text;
        if (original is not null && spoken.PressEnter)
        {
            // The Original is shown and restored as text too, so it loses the command the same way.
            original = PressEnterDetector.Detect(original).Text;
        }

        InjectionResult result;
        if (text.Length == 0)
        {
            // Only "press enter" was said: send it, e.g. for a message typed by hand. There is
            // nothing to keep or paste again.
            result = InjectionResult.Injected;
        }
        else
        {
            // Kept before the paste is attempted: the case worth covering is exactly the one where
            // the paste doesn't land, and Paste Again or the Dictations screen can then recover it.
            _collaborators.LastTranscription.Set(text);
            _collaborators.History.Add(text, dictation.AppName, original);
            result = await _collaborators.Injector.InjectAsync(text).ConfigureAwait(true);
        }

        switch (result)
        {
            case InjectionResult.Injected:
                // Only after a paste that landed: Enter into the wrong window, or with the text
                // missing, would send something the user never meant to send.
                if (spoken.PressEnter)
                {
                    PressEnter();
                }

                _collaborators.SoundCues.Play(SoundCue.Done);
                if (dictation.HitLimit)
                {
                    ShowNotice(OverlayNotice.RecordingLimitReached);
                }
                else
                {
                    ShowForAWhile(new OverlayView { Phase = OverlayPhase.Pasted }, PastedDuration);
                }

                break;

            default:
                ShowPasteNotice(result);
                break;
        }
    }

    private void PressEnter()
    {
        try
        {
            _collaborators.Injector.PressEnter();
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("PressEnter", ex);
        }
    }

    private static readonly OverlayView TranscribingView =
        new() { Phase = OverlayPhase.Transcribing, Controls = OverlayControls.Cancel };

    private void ShowNotice(OverlayNotice notice, string? key = null) =>
        ShowForAWhile(new OverlayView { Phase = OverlayPhase.Notice, Notice = notice, NoticeKey = key }, NoticeDuration);

    /// <summary>Shows a passing outcome or message, then returns to rest unless something else
    /// has replaced it by then.</summary>
    private void ShowForAWhile(OverlayView view, TimeSpan duration)
    {
        SetView(view);
        _pendingReturn = _collaborators.Timer.Schedule(duration, ReturnToRest);
    }

    private void ReturnToRest() => ReturnToRest(afterCancel: false);

    private void ReturnToRest(bool afterCancel) =>
        SetView(_rest == OverlayPhase.Ready
            ? new OverlayView { Phase = OverlayPhase.Ready, Controls = OverlayControls.Start, AfterCancel = afterCancel }
            : (_alwaysShow ? OverlayView.Handle : OverlayView.Hidden) with { AfterCancel = afterCancel });

    /// <summary>True once <paramref name="dictation"/> was cancelled, so an await that returns
    /// late must not touch the Overlay or deliver anything.</summary>
    private bool IsSuperseded(ActiveDictation dictation) =>
        dictation.Cancellation.IsCancellationRequested || !ReferenceEquals(_active, dictation);

    /// <summary>The only way the view changes. Any view change also cancels a pending timed
    /// return, which belonged to the view being replaced.</summary>
    private void SetView(OverlayView view)
    {
        _pendingReturn?.Dispose();
        _pendingReturn = null;
        // Also while a notice sits over a running transcription: Esc is still its Cancel.
        _swallowEscape = view.Phase is OverlayPhase.Listening or OverlayPhase.Transcribing || _active is not null;
        _isListening = view.Phase == OverlayPhase.Listening;

        if (view == View)
        {
            return;
        }

        View = view;
        ViewChanged?.Invoke(view);
    }
}
