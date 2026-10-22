using System.Runtime.InteropServices;
using System.Windows.Forms;
using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Overlay;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Setup;
using VoiceCtrl.Core.Status;
using VoiceCtrl.Core.Transcription;
using VoiceCtrl.Core.Updates;
using VoiceCtrl.Dictation;
using VoiceCtrl.Hub;
using VoiceCtrl.Input;
using VoiceCtrl.Overlay;
using VoiceCtrl.Setup;
using VoiceCtrl.Tray;
using VoiceCtrl.Updates;
using VoiceCtrl.Web;

namespace VoiceCtrl;

/// <summary>
/// The running app (spec §2): owns the Dictation engine, the input thread, the tray icon, the
/// WebView2 runtime and the open surfaces, and lives until Quit. Created by
/// <see cref="Program"/> once the single-instance check and settings are done.
/// </summary>
internal sealed class VoiceCtrlContext : ApplicationContext
{
    private readonly SynchronizationContext _ui;
    private readonly SettingsStore _settings;
    private readonly SecretsStore _secrets;
    private readonly DictationHost _dictation;
    private readonly MicrophoneCatalog _microphones;
    private readonly AppStatusTracker _status;
    private readonly InputThread _input;
    private readonly PasteAgainHotkey _pasteAgain;
    private readonly DictationSettingsCommands _dictationSettings;
    private readonly TranscriptionSettingsService _transcriptionRules;
    private readonly TranscriptionSettingsCommands _transcriptionSettings;
    private readonly LastAppWindow _lastApp;
    private readonly DictationsCommands _dictations;
    private readonly UpdateService _updates;
    private readonly System.Threading.Timer _updateSchedule;
    private readonly StartupMarker _startupMarker;
    private readonly GeneralSettingsCommands _generalSettings;
    private readonly TrayIcon _tray;
    private readonly SurfaceAppearance _appearance;
    private readonly AppCommands _appCommands;
    private readonly PersonalizationCommands _personalizationCommands;
    private readonly ProfileCommands _profileCommands;
    private readonly StyleCommands _styleCommands;
    private readonly DictionarySuggestions _suggestions;
    private readonly SuggestionCommands _suggestionCommands;
    private WebViewRuntime? _runtime;
    // Volatile: the keyboard hook's thread reads it for Esc during a drag.
    private volatile OverlayHost? _overlay;
    private SurfaceForm? _hub;
    private SetupWindow? _setup;
    private TrayFlyout? _flyout;
    private bool _hubRequestedBeforeRuntime;
    private ShowHubScreen? _hubTargetBeforeRuntime;
    private bool _quitting;
    private bool _disposed;

    /// <summary>First check 30 s after start, then every 6 h (spec §9).</summary>
    private static readonly TimeSpan FirstUpdateCheck = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

    /// <summary>The tray flyout is made this long after start, then kept warm (spec §2).</summary>
    private static readonly TimeSpan FlyoutWarmUp = TimeSpan.FromSeconds(5);

    public VoiceCtrlContext(SettingsStore settings, SecretsStore secrets, SingleInstance instance, bool autostart, UpdateFeed updateFeed, StartupMarker startupMarker)
    {
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Start the context on the UI thread.");
        _settings = settings;
        _secrets = secrets;
        _startupMarker = startupMarker;

        // Step 4: Core services, then the input thread with both hooks.
        AppConfig config = ConfigLoader.Load(settings.Current, secrets.GeminiApiKey);
        var modeStore = new TranscriptionModeStore(settings);
        // Ticket 60: one key's health per Online provider; the card speaks for the chosen one.
        var keyHealth = new ApiKeyHealth(() => settings.Current.Transcription.Provider ?? ProviderMigration.Default);
        _microphones = new MicrophoneCatalog();
        _dictation = new DictationHost(config, settings, modeStore, secrets, keyHealth,
            () => _microphones.Current.RecordingDevice(_settings.Current.Dictation.MicrophoneId),
            () => _settings.Current.Dictation.SoundCues,
            settings.Current.Privacy.KeepDictations);
        _status = new AppStatusTracker(settings, _microphones, keyHealth);

        // A newly saved key (Setup, later Settings) gets a fresh chance: the card clears at once
        // and Auto tries Online again.
        secrets.Changed += keyHealth.Reset;

        // While paused, the gestures that start something do nothing (DESIGN §12). Ending a hold
        // and Esc still pass, so a Dictation already under way can finish or be cancelled. Esc
        // during an Overlay drag puts the Overlay back and cancels nothing.
        _input = new InputThread(_ui, config.DoubleTapWindowMs, settings.Current.Dictation.Hotkeys,
            () => _dictation.Controller.ShouldSwallowEscape || (_overlay?.IsDragging ?? false));
        _input.HotkeyDoubleTapped += () => { if (!_status.Paused) { _ = _dictation.Controller.ToggleBar(); } };
        _input.TriggerTapped += () => { if (!_status.Paused) { _ = _dictation.Controller.Tap(); } };
        _input.TriggerHoldStarted += () => { if (!_status.Paused) { _ = _dictation.Controller.HoldStart(); } };
        _input.TriggerHoldEnded += () => _ = _dictation.Controller.HoldEnd();
        _input.RewriteHoldStarted += () => { if (!_status.Paused) { _ = _dictation.Controller.RewriteHoldStart(); } };
        _input.RewriteHoldEnded += () => _ = _dictation.Controller.RewriteHoldEnd();
        _input.RewriteHoldCancelled += () => _dictation.Controller.CancelRewrite();
        _input.EscapePressed += () =>
        {
            if (_overlay?.CancelDrag() != true)
            {
                _dictation.Controller.Cancel();
            }
        };
        _input.Start();

        // Paste Again pastes into whatever has focus; it never starts a Dictation, so Pause leaves it be.
        _pasteAgain = new PasteAgainHotkey();
        _pasteAgain.Apply(settings.Current.Dictation.PasteAgain);
        _pasteAgain.Pressed += () => _ = _dictation.Controller.PasteAgain();

        // Settings apply live: the hooks and the Paste Again key follow every saved change. The
        // Hub's view of them subscribes after this, so it reads the key as already registered.
        settings.DictationChanged += section => RunOnUi(() =>
        {
            _input.Rebind(section.Hotkeys);
            _pasteAgain.Apply(section.PasteAgain);
        });
        _dictationSettings = new DictationSettingsCommands(settings, _microphones, _input, _pasteAgain, _ui);

        // Mode, the Online provider and its key, Default cleanup and the Offline language packs. A key
        // is checked against the provider's real service before Settings saves it.
        _transcriptionRules = new TranscriptionSettingsService(
            settings, secrets, keyHealth, _dictation.Packs, _dictation.SmartPacks,
            (endpoint, key, cancellationToken) => OnlineKeyValidator.CheckAsync(endpoint, key, cancellationToken: cancellationToken),
            config.GeminiModelId);
        _transcriptionSettings = new TranscriptionSettingsCommands(_transcriptionRules, _dictation.Packs, _dictation.SmartPacks, _ui);

        // Paste Again from the Hub pastes into the app used before the Hub, so that app is followed
        // from the start, before the Hub can ever open.
        _lastApp = new LastAppWindow();
        _dictations = new DictationsCommands(_dictation, settings, _lastApp, _ui);

        // Sound and Overlay, Privacy and App. Updates come from GitHub Releases through Velopack,
        // checked against the signed manifest; Restart now quits the normal way.
        _updates = new UpdateService(settings, new VelopackUpdateSource(updateFeed, () => _ui.Post(_ => Quit(), null)),
            AppCommands.Version, () => DateTimeOffset.Now);
        _generalSettings = new GeneralSettingsCommands(settings, secrets, _dictation, _pasteAgain, _updates,
            () => ApplyStartWithWindows(_settings), _ui);
        _updateSchedule = new System.Threading.Timer(_ => _ = _updates.ScheduledCheckAsync(), null, FirstUpdateCheck, UpdateCheckInterval);

        // Step 5: the tray icon, then the WebView2 environment and the Overlay (async).
        _tray = new TrayIcon(_ui);
        _tray.HubRequested += () => OpenHub();
        _tray.FlyoutRequested += ToggleFlyout;

        instance.ActivateRequested += () => _ui.Post(_ => OpenHub(), null);
        instance.QuitRequested += () => _ui.Post(_ => Quit(), null);

        ApplyStartWithWindows(settings);

        // On a first run the prewarm waits for Setup: in Auto it would start the 700 MB offline
        // download before the user has even chosen Online or Offline.
        if (settings.Current.App.SetupComplete)
        {
            _dictation.Prewarm();
            if (UserDataPaths.SandboxRoot is null)
            {
                // Skipped Setup or an older install: the switch is on, so the model it needs is fetched.
                StartSmartCleanupDownload();
            }
        }

        _appearance = new SurfaceAppearance(settings, _ui);
        _appCommands = new AppCommands(_appearance, new ThemeStore(settings), _status, settings, () => DateTimeOffset.Now);
        _personalizationCommands = new PersonalizationCommands(PersonalizationLibrary.CreateDefault(), () => _hub);
        _profileCommands = new ProfileCommands(
            new ProfileLibrary(UserDataPaths.Profiles, () => settings.Current.Style.AppKinds), () => settings.Current.Style.AppKinds);
        _styleCommands = new StyleCommands(new StyleSettingsService(settings, word => BuiltInVocabulary.Shared.IsOrdinaryWord(word)));

        // Dictionary Suggestions: mined from the kept Dictations when the Dictionary screen asks,
        // ranked by TypeSafe only with the user's own key and never in Offline Mode.
        _suggestions = new DictionarySuggestions(settings, secrets, PersonalizationLibrary.CreateDefault(),
            () => _dictation.History.Entries.Select(e => e.Text).ToList(),
            new TypeSafeClient(new System.Net.Http.HttpClient()), UserDataPaths.IgnoredSuggestions);
        _suggestionCommands = new SuggestionCommands(_suggestions, _ui);
        _ = StartWebViewAsync();

        // The status's sources change on audio and transcription threads. The page gets the
        // status as it is when the post arrives, so a burst of changes can't land out of order.
        _status.Changed += changed => _ui.Post(_ =>
        {
            _runtime?.Broadcast(new StatusChanged(_status.Current));
            _tray.SetPaused(_status.Paused);
        }, null);

        // Step 6. First run → Setup (OpenHub routes there until Setup is done); --autostart
        // (from the Run key) stays in the tray; otherwise the Hub.
        if (!autostart)
        {
            OpenHub();
        }
    }

    /// <summary>
    /// Makes the Run value match Start with Windows, on every start and on each change (spec §9).
    /// The Start Menu shortcut is the installer's (InstallerHooks). The sandbox harness never
    /// touches the Run value.
    /// </summary>
    /// <summary>
    /// Smart cleanup's model downloads in the background once Setup is saved, and Settings shows how
    /// far it is. Setup saves the switch: always on for Offline, the listener's answer for Online.
    /// </summary>
    private void StartSmartCleanupDownload()
    {
        TranscriptionSection section = _settings.Current.Transcription;
        if (!section.SmartCleanup || _dictation.SmartPacks.State is not { Installed: null, Target: null })
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _dictation.SmartPacks.Pick(section.SmartCleanupTier);
            }
            catch (Exception ex)
            {
                SimpleFileLogger.LogError("Smart cleanup download after Setup", ex);
            }
        });
    }

    private static void ApplyStartWithWindows(SettingsStore settings)
    {
        if (UserDataPaths.SandboxRoot is not null)
        {
            return;
        }

        try
        {
            AutoStartManager.Apply(settings.Current.App.StartWithWindows);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            SimpleFileLogger.LogError("Start with Windows", ex);
        }
    }

    private async Task StartWebViewAsync()
    {
        try
        {
            _runtime = await WebViewRuntime.CreateAsync(_appearance);
        }
        catch (Exception ex)
        {
            // The installer puts the runtime in place (spec §9); this is the last resort.
            SimpleFileLogger.LogError("WebView2 environment", ex);
            MessageBox.Show(WebViewRuntime.MissingRuntimeMessage, "VoiceCtrl", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Quit();
            return;
        }

        // Kept warm for the whole run (spec §2). Started without waiting, so the Hub isn't held up.
        _overlay = new OverlayHost(_runtime, _ui, _dictation.Controller, _input, _settings, _appCommands.Register,
            () => _status.Paused, () => _appearance.ReducedMotion);
        _overlay.KeySettingsRequested += () => OpenHub(new ShowHubScreen("settings", "api-key"));
        _ = _overlay.StartAsync();

        // The hooks, the tray and WebView2 are up: this start counts as a success (spec §9).
        _startupMarker.Complete();
        _ = WarmUpFlyoutAsync();

        if (_hubRequestedBeforeRuntime)
        {
            OpenHub(_hubTargetBeforeRuntime);
        }
    }

    private async Task WarmUpFlyoutAsync()
    {
        await Task.Delay(FlyoutWarmUp);
        EnsureFlyout();
    }

    /// <summary>The tray flyout, made on first need if the warm-up hasn't made it yet. Its page
    /// shares the Hub's app.* commands and its Mode command.</summary>
    private TrayFlyout? EnsureFlyout()
    {
        if (_flyout is not null || _runtime is null || _quitting)
        {
            return _flyout;
        }

        _flyout = new TrayFlyout(_runtime, _ui,
            dispatcher =>
            {
                _appCommands.Register(dispatcher);
                dispatcher.Handle<SetMode>(args =>
                {
                    if (!Enum.IsDefined(args.Mode))
                    {
                        throw new BridgeCommandException("bad_args", "Unknown Mode.");
                    }

                    _transcriptionRules.SetMode(args.Mode);
                    return Task.CompletedTask;
                });
            },
            () => (_settings.Current.Transcription.Mode, _dictation.LastTranscription.Text is not null));
        _flyout.HubRequested += () => OpenHub();
        _flyout.SettingsRequested += () => OpenHub(new ShowHubScreen("settings", null));
        _flyout.CopyLatestRequested += CopyLatest;
        _flyout.QuitRequested += Quit;
        _ = _flyout.StartAsync();
        return _flyout;
    }

    private void ToggleFlyout(PixelRect icon) => EnsureFlyout()?.Toggle(icon);

    /// <summary>The flyout's Copy latest Transcription. A busy clipboard is said on the Overlay
    /// (DESIGN §12: no Windows notifications).</summary>
    private void CopyLatest()
    {
        if (_dictation.LastTranscription.Text is not { } text)
        {
            return;
        }

        try
        {
            Clipboard.SetDataObject(text, copy: true, retryTimes: 5, retryDelay: 60);
        }
        catch (ExternalException ex)
        {
            SimpleFileLogger.LogError("Copy latest", ex);
            _dictation.Controller.ReportCopyFailed();
        }
    }

    /// <summary>Opens the Hub, or brings it forward, on Dictations or on <paramref name="target"/>.
    /// The Hub is created on open and disposed on close (CONTEXT: it only exists in memory while
    /// open). Until Setup is done, this opens Setup instead: a first run, or a tray click after an
    /// --autostart first run.</summary>
    private void OpenHub(ShowHubScreen? target = null)
    {
        if (_quitting)
        {
            return;
        }

        if (_runtime is null)
        {
            _hubRequestedBeforeRuntime = true;
            _hubTargetBeforeRuntime = target;
            return;
        }

        _hubRequestedBeforeRuntime = false;
        _hubTargetBeforeRuntime = null;
        if (_setup is not null || !_settings.Current.App.SetupComplete)
        {
            OpenSetup(_runtime);
            return;
        }

        if (_hub is not null)
        {
            if (_hub.WindowState == FormWindowState.Minimized)
            {
                _hub.WindowState = FormWindowState.Normal;
            }

            if (target is not null)
            {
                _hub.Post(target);
            }

            _hub.Activate();
            return;
        }

        // A Hub that isn't open yet can't take an event before its page subscribes, so it reads
        // the target from its URL instead.
        SurfacePage page = target is null
            ? SurfacePage.Hub
            : SurfacePage.Hub with { File = $"{SurfacePage.Hub.File}?screen={Uri.EscapeDataString(target.Screen)}&row={Uri.EscapeDataString(target.Row ?? "")}" };
        _hub = new SurfaceForm(_runtime, page, dispatcher =>
        {
            _appCommands.Register(dispatcher);
            _dictationSettings.Register(dispatcher);
            _transcriptionSettings.Register(dispatcher);
            _dictations.Register(dispatcher);
            _generalSettings.Register(dispatcher);
            _personalizationCommands.Register(dispatcher);
            _profileCommands.Register(dispatcher);
            _styleCommands.Register(dispatcher);
            _suggestionCommands.Register(dispatcher);
        });
        _dictationSettings.Attach(_hub);
        _generalSettings.Attach(_hub);
        _transcriptionSettings.Attach(_hub);
        _dictations.Attach(_hub);
        _suggestionCommands.Attach(_hub);
        _hub.FormClosed += (_, _) =>
        {
            _hub?.Dispose();
            _hub = null;
        };
        _hub.Show();
        _hub.Activate();
    }

    private void OpenSetup(WebViewRuntime runtime)
    {
        if (_setup is not null)
        {
            _setup.Activate();
            return;
        }

        SimpleFileLogger.LogInfo("First run: opening Setup");
        _setup = new SetupWindow(
            runtime, _settings, _secrets,
            dispatcher =>
            {
                _appCommands.Register(dispatcher);

                // The hotkey step changes the talk and rewrite keys with Settings' own capture.
                _dictationSettings.Register(dispatcher);
            },
            StartSmartCleanupDownload);
        _dictationSettings.Attach(_setup.Surface);
        _setup.Ended += finished =>
        {
            SetupWindow? setup = _setup;
            _setup = null;
            setup?.Dispose();

            // Closing early counts as skipping (spec §7): saved, but no Hub.
            if (finished)
            {
                ApplyStartWithWindows(_settings);
                OpenHub();
            }

            _dictation.Prewarm();
        };
        _setup.Show();
    }

    /// <summary>Settings are saved on the UI thread today; this keeps the window-bound work there
    /// if a later caller saves from elsewhere.</summary>
    private void RunOnUi(Action action)
    {
        if (SynchronizationContext.Current == _ui)
        {
            action();
        }
        else
        {
            _ui.Post(_ => action(), null);
        }
    }

    private void Quit()
    {
        if (_quitting)
        {
            return;
        }

        _quitting = true;
        _flyout?.Hide();
        _setup?.CloseForQuit();
        _hub?.Close();
        ExitThread();
    }

    /// <summary>Runs once: WinForms disposes the context when the message loop ends, and
    /// <see cref="Program"/>'s <c>using</c> disposes it again, and some of what it owns (the
    /// microphone catalog's device enumerator) throws on a second dispose.</summary>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (disposing)
        {
            _overlay?.Dispose();
            _flyout?.Dispose();
            _dictationSettings.Dispose();
            _transcriptionSettings.Dispose();
            _transcriptionRules.Dispose();
            _dictations.Dispose();
            _generalSettings.Dispose();
            _suggestionCommands.Dispose();
            _suggestions.Dispose();
            _updateSchedule.Dispose();
            _updates.Dispose();
            _lastApp.Dispose();
            _pasteAgain.Dispose();
            _input.Dispose();
            _tray.Dispose();
            _status.Dispose();
            _microphones.Dispose();
            _dictation.Dispose();
            _appearance.Dispose();
        }

        base.Dispose(disposing);
    }
}
