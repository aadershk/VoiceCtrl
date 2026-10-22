using System.Diagnostics;
using System.IO;
using System.Net.Http;
using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.EmailRequests;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Rewrite;
using VoiceCtrl.Core.Status;
using VoiceCtrl.Core.Transcription;
using VoiceCtrl.Input;

namespace VoiceCtrl.Dictation;

/// <summary>
/// Builds the Dictation engine with the real Windows collaborators (spec §5.1) and owns their
/// lifetimes.
/// </summary>
internal sealed class DictationHost : IDisposable
{
    private readonly WasapiAudioRecorder _recorder;
    private readonly AdaptiveTranscriptionClient _transcription;
    private readonly WavSoundCues _soundCues;
    private readonly QueuedAudioDucker _ducker;
    private readonly SmartCleanupService _smartCleanup;
    private readonly AdaptiveRewriteClient? _rewriter;
    private readonly EmailWriter _emailWriter;
    private readonly HttpClient _packDownloads = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly AppConfig _config;
    private readonly SettingsStore _settings;

    /// <summary>The Online providers as Settings has them: the chosen one, the keys, the fallback order.</summary>
    public OnlineAccess Online { get; }

    /// <param name="microphoneId">Read at each Dictation's start: the chosen microphone while it is
    /// connected, or null for the Windows default.</param>
    /// <param name="soundCues">Read at each cue: the Sound Cues setting.</param>
    /// <param name="keep">Keep Dictations as saved; later changes go to <see cref="History"/>.</param>
    public DictationHost(AppConfig config, SettingsStore settings, TranscriptionModeStore modeStore, SecretsStore secrets, ApiKeyHealth keyHealth, Func<string?> microphoneId, Func<bool> soundCues, KeepDictations keep)
    {
        _config = config;
        _settings = settings;
        _recorder = new WasapiAudioRecorder(microphoneId);
        _soundCues = new WavSoundCues(soundCues);

        // Creates the three user-editable files with starter content if they're missing.
        PersonalizationStore personalization = Personalization = PersonalizationStore.CreateDefault();

        // Smart cleanup (ticket 37): the local language model that edits Offline text. Half the
        // logical cores, at most 8: more threads do not decode faster, and the PC stays usable.
        var smartClock = Stopwatch.StartNew();
        SmartPacks = new SmartCleanupPackManager(
            settings,
            UserDataPaths.Models,
            (model, progress, token) => LocalModelDownloader.DownloadAllAsync(model.PathUnder(UserDataPaths.Models), _packDownloads, progress, token, model: model),
            () =>
            {
                _smartCleanup?.Unload();
                return Task.CompletedTask;
            },
            () => smartClock.Elapsed);
        _smartCleanup = new SmartCleanupService(settings, SmartPacks, Math.Clamp(Environment.ProcessorCount / 2, 2, 8));
        Ledger = new SmartCleanupLedger();

        // The engine record (VED-77): off unless its Settings switch is on. A record left by a run
        // with the switch on is deleted at start when it is off now, and again whenever it is turned off.
        EngineRecorder = new EngineRecorder(UserDataPaths.EngineRecord, () => settings.Current.Privacy.EngineRecord);
        EngineRecorder.ClearIfOff();
        settings.PrivacyChanged += _ => EngineRecorder.ClearIfOff();

        // Indexing the built-in vocabulary takes a moment; do it now, off this thread, so the first
        // Dictation never waits for it.
        _ = BuiltInVocabulary.Warm();

        // The provider, its key, the default Cleanup Level and the Offline model are read live, so a
        // change in Settings (or Setup) applies to the next Dictation without a restart. Online is
        // the chosen provider, then the others with a saved key (ticket 60).
        Online = OnlineAccess.From(settings, secrets, keyHealth, config.GeminiModelId);
        _transcription = AdaptiveTranscriptionClient.Create(
            config, modeStore, Online, personalization,
            isOfflineReady: () =>
            {
                OfflineModel model = CurrentOfflineModel();
                return LocalModelDownloader.IsFullyDownloaded(model.PathUnder(UserDataPaths.Models), model);
            },
            cleanupLevel: () => settings.Current.Transcription.DefaultCleanup,
            offlineModel: CurrentOfflineModel,
            pressEnter: () => settings.Current.Dictation.PressEnter,
            smartCleanup: _smartCleanup,
            ledger: Ledger,
            engineRecorder: EngineRecorder,
            styles: () => settings.Current.Style);

        // Rewrite by voice (ticket 39): the same Mode rules as a Dictation, the same speech and
        // language models. Without an offline speech client (a test double) the feature stays off.
        if (_transcription.OfflineInstructions is { } instructions)
        {
            _rewriter = AdaptiveRewriteClient.Create(config, modeStore, personalization, Online, instructions, _smartCleanup);
        }

        // An Email request said into an email app is written as the email (ticket 57), by Gemini or the
        // Smart cleanup model under the same Mode rules as a rewrite by voice.
        _emailWriter = EmailWriter.Create(
            config, modeStore, personalization, Online, _smartCleanup,
            () => settings.Current.Transcription.DefaultCleanup, () => settings.Current.Style);

        // First on the queue, before any Dictation can lower anything: levels a crashed run left
        // lowered go back up.
        _ducker = new QueuedAudioDucker(AudioDucker.CreateDefault(() => settings.Current.Dictation.MuteOtherAudio));
        _ducker.RestoreAfterCrash();

        var clock = Stopwatch.StartNew();
        Packs = new OfflinePackManager(
            settings,
            UserDataPaths.Models,
            (model, progress, token) => LocalModelDownloader.DownloadAllAsync(model.PathUnder(UserDataPaths.Models), _packDownloads, progress, token, model: model),
            _transcription.UnloadOfflineModelAsync,
            () => clock.Elapsed);

        // Loads the new model as soon as a pack is installed or removed, unless Online never needs it.
        Packs.InstalledChanged += () =>
        {
            if (modeStore.Current != TranscriptionModePreference.Online)
            {
                _ = _transcription.PrewarmConnectionAsync();
            }
        };

        // A Smart cleanup model that just finished downloading starts loading at once.
        SmartPacks.InstalledChanged += () =>
        {
            if (modeStore.Current != TranscriptionModePreference.Online)
            {
                _ = _transcription.PrewarmConnectionAsync();
            }
        };

        // Re-warms the offline model when Mode switches to Offline or Auto mid-session.
        modeStore.SwitchedToOfflineOrAuto += () => _ = _transcription.PrewarmConnectionAsync();

        History = DictationHistoryStore.Load(keep);
        LastTranscription = new LastTranscriptionStore();

        // The Latest hero says "or press Ctrl Shift V", so after a restart the key pastes the
        // Dictation the hero shows, not nothing.
        if (History.Entries.FirstOrDefault() is { } newest)
        {
            LastTranscription.Set(newest.Text);
        }

        Controller = new DictationController(new DictationCollaborators
        {
            Recorder = _recorder,
            Transcription = _transcription,
            OfflineTranscription = _transcription.OfflineOnly,
            // Retry offline would otherwise start a 700 MB download mid-retry.
            IsOfflineModelInstalled = () =>
            {
                OfflineModel model = CurrentOfflineModel();
                return LocalModelDownloader.IsFullyDownloaded(model.PathUnder(UserDataPaths.Models), model);
            },
            Injector = new ClipboardPasteInjector(),
            Rewriter = _rewriter,
            EmailWriter = _emailWriter,
            Selection = WindowsSelection.Create(),
            ForegroundApp = new ForegroundAppName(),
            SoundCues = _soundCues,
            Ducker = _ducker,
            Timer = new UiThreadDictationTimer(),
            LastTranscription = LastTranscription,
            History = History,
            IsApiKeyMissing = () => modeStore.Current == TranscriptionModePreference.Online && !Online.HasAny,
            PressEnter = () => settings.Current.Dictation.PressEnter,
            // A cancelled recording is kept only while Dictations are (ADR-0004).
            KeepCancelledRecordings = () => settings.Current.Privacy.KeepDictations != KeepDictations.None,
            TakeOriginal = Ledger.Take,
            PasteAgainKeyLabel = () => string.Join("+", settings.Current.Dictation.PasteAgain.Chips),
        });

        // The device can stop on its own mid-dictation; the recorder says so on its capture thread,
        // and the engine wants it on this one.
        SynchronizationContext? ui = SynchronizationContext.Current;
        _recorder.Faulted += () =>
        {
            if (ui is null)
            {
                Controller.MicrophoneStopped();
            }
            else
            {
                ui.Post(_ => Controller.MicrophoneStopped(), null);
            }
        };

        // The Overlay renders every view; the log keeps the phase for diagnostics.
        Controller.ViewChanged += view => SimpleFileLogger.LogInfo($"Dictation: {view.Phase}");
    }

    public DictationController Controller { get; }

    /// <summary>The extra Offline language pack: install, replace, remove.</summary>
    public OfflinePackManager Packs { get; }

    /// <summary>Smart cleanup's language model: download, replace, remove.</summary>
    public SmartCleanupPackManager SmartPacks { get; }

    /// <summary>What Smart cleanup replaced in the Dictation that just finished.</summary>
    public SmartCleanupLedger Ledger { get; }

    /// <summary>What the Offline engine did per Dictation, when the Engine record switch is on.</summary>
    public EngineRecorder EngineRecorder { get; }

    /// <summary>The model Offline runs now: the installed language pack, else English (in the folder
    /// the advanced <c>LocalModelVariant</c> names, if it overrides the default).</summary>
    private OfflineModel CurrentOfflineModel() =>
        _settings.Current.Transcription.OfflinePack != OfflinePack.None && Packs.State.Installed != OfflinePack.None
            ? Packs.CurrentModel()
            : OfflineModels.English with { Directory = _config.LocalModelVariant };

    public DictationHistoryStore History { get; }

    /// <summary>The Dictionary, Snippets and Profiles as loaded; Copy diagnostics counts them.</summary>
    public PersonalizationStore Personalization { get; }

    /// <summary>Settings' Play: the start and done cues, whatever the switch says.</summary>
    public void PreviewSoundCues() => _soundCues.PlayPreview();

    public LastTranscriptionStore LastTranscription { get; }

    /// <summary>Warms the offline model ahead of the first Dictation. Never awaited: a slow first
    /// download must not delay the tray or the hooks.</summary>
    public void Prewarm() => _ = _transcription.PrewarmConnectionAsync();

    public void Dispose()
    {
        _ducker.Dispose();
        _transcription.Dispose();
        _rewriter?.Dispose();
        _emailWriter.Dispose();
        _smartCleanup.Dispose();
        _packDownloads.Dispose();
        _recorder.Dispose();
        _soundCues.Dispose();
    }

    /// <summary>The engine's clock on the UI thread: a WinForms timer ticks where it was started,
    /// and the engine only schedules from the UI thread.</summary>
    private sealed class UiThreadDictationTimer : IDictationTimer
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public IDisposable Schedule(TimeSpan delay, Action callback)
        {
            var timer = new System.Windows.Forms.Timer { Interval = Math.Max(1, (int)delay.TotalMilliseconds) };
            timer.Tick += (_, _) =>
            {
                timer.Dispose();
                callback();
            };
            timer.Start();
            return timer;
        }
    }

    /// <summary>The foreground app's display name (spec §5.3).</summary>
    private sealed class ForegroundAppName : IForegroundApp
    {
        public string? CurrentAppName() => LastAppWindow.AppName(NativeMethods.GetForegroundWindow());
    }

    /// <summary>
    /// Mute Other Audio off the UI thread. Walking every app's audio session takes tens of
    /// milliseconds, which would hold up the Overlay at the start of a Dictation; one queue keeps
    /// each Restore after its Duck.
    /// </summary>
    private sealed class QueuedAudioDucker(AudioDucker ducker) : IAudioDucker, IDisposable
    {
        // Enough for a Restore stuck behind a slow Duck; quitting must not hang on the audio service.
        private static readonly TimeSpan QuitWait = TimeSpan.FromSeconds(2);

        private readonly object _gate = new();
        private Task _tail = Task.CompletedTask;

        public void Duck() => Enqueue(ducker.Duck);

        public void Restore() => Enqueue(ducker.Restore);

        public void RestoreAfterCrash() => Enqueue(ducker.RestoreAfterCrash);

        /// <summary>Quitting mid-Dictation: other apps go back up before VoiceCtrl exits. If this
        /// times out, ducking.json is still there for the next start.</summary>
        public void Dispose()
        {
            Enqueue(ducker.Restore);
            Task tail;
            lock (_gate)
            {
                tail = _tail;
            }

            tail.Wait(QuitWait);
        }

        // AudioDucker never throws, so no step can fault the queue for the ones after it.
        private void Enqueue(Action work)
        {
            lock (_gate)
            {
                _tail = _tail.ContinueWith(_ => work(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }
    }
}
