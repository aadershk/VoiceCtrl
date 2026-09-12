using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using VoiceCtrl.Bootstrap;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using VoiceCtrl.Hub;
using VoiceCtrl.Overlay;
using VoiceCtrl.Tray;

namespace VoiceCtrl;

public partial class App : Application
{
    // Session-local (no "Global\" prefix): VoiceCtrl only ever runs per-user, so scoping the names
    // to the caller's own session is enough to catch a second launch and needs no elevated rights.
    private const string InstanceMutexName = "VoiceCtrl.SingleInstanceMutex";
    private const string ActivateEventName = "VoiceCtrl.ActivateRequestedEvent";
    private const string QuitEventName = "VoiceCtrl.QuitRequestedEvent";

    private LowLevelKeyboardHook? _hook;
    private OverlayWindow? _overlay;
    private ITranscriptionClient? _transcriptionClient;
    private TranscriptionModeStore? _modeStore;
    private TrayIconManager? _trayIconManager;
    private HubWindow? _hubWindow;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;
    private EventWaitHandle? _quitEvent;
    private RegisteredWaitHandle? _activateRegisteredWait;
    private RegisteredWaitHandle? _quitRegisteredWait;
    private string _envPath = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Every earlier launch just built another tray icon, hook, and Overlay on top of whatever
        // was already running — double-clicking the exe from Explorer looks like nothing happened
        // (there is no window to notice), so a confused user would launch it repeatedly, stacking
        // up tray icons. A named Mutex makes a second launch recognize the first instance instead
        // of becoming one.
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Ask whatever is running to quit and wait to inherit the Mutex, rather than just
            // deferring to it — otherwise, launching a newer build while an older one is still
            // running would silently bring the *old* one's window forward and exit, which looks
            // exactly like "the update didn't happen" (this is what prompted this whole block).
            try
            {
                using EventWaitHandle existingQuit = EventWaitHandle.OpenExisting(QuitEventName);
                existingQuit.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // Nothing listening for it: either a pre-this-version build with no quit listener
                // at all, or the running instance is mid-startup/mid-exit. Either way, fall through
                // to the timeout path below rather than erroring out.
            }

            bool tookOver;
            try
            {
                tookOver = _instanceMutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                // The previous instance quit without releasing the Mutex cleanly (crashed, or was
                // killed) — its abandonment hands ownership to us, which is just as good as a clean
                // release for this purpose.
                tookOver = true;
            }

            if (!tookOver)
            {
                // Most likely an old build with no quit listener to signal. Fall back to just
                // bringing whatever is running to the front instead of leaving both launch attempts
                // stuck: not a full replace, but still better than silently doing nothing.
                try
                {
                    using EventWaitHandle existingActivate = EventWaitHandle.OpenExisting(ActivateEventName);
                    existingActivate.Set();
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                }

                Shutdown();
                return;
            }

            // We now own the Mutex: continue starting up exactly as the first instance would.
        }

        _envPath = Path.Combine(AppContext.BaseDirectory, ".env");
        bool isFirstRun = FirstRunSetup.Run(_envPath);
        StartMenuShortcut.EnsureExists();

        AppConfig config = ConfigLoader.Load(_envPath);
        _modeStore = TranscriptionModeStore.Load(config);

        // Creates the three user-editable files with commented starter content if they are not
        // there yet, so the tray entries below always open something that explains itself.
        PersonalizationStore personalization = PersonalizationStore.CreateDefault();
        var lastTranscription = new LastTranscriptionStore();
        DictationHistoryStore history = DictationHistoryStore.Load();

        var adaptiveClient = new AdaptiveTranscriptionClient(config, _modeStore, personalization);
        adaptiveClient.FellBackToOffline += () => _overlay?.ShowFellBackToOfflineNotice();
        _transcriptionClient = adaptiveClient;

        var textInjector = new ClipboardPasteInjector();
        _overlay = new OverlayWindow(config, _transcriptionClient, _modeStore, textInjector, lastTranscription, history);

        _trayIconManager = new TrayIconManager(_modeStore, lastTranscription,
        [
            new TrayFileEntry("Dictionary...", UserDataPaths.Dictionary),
            new TrayFileEntry("Snippets...", UserDataPaths.Snippets),
            new TrayFileEntry("App profiles...", UserDataPaths.Profiles),
        ]);
        _trayIconManager.HubRequested += () => OpenHub(history);
        _trayIconManager.SettingsRequested += () =>
            _ = FirstRunSetup.OpenInNotepadAndNotifyOnCloseAsync(_envPath, () => _trayIconManager?.ShowSettingsRestartNotice());
        _trayIconManager.OpenFileRequested += FirstRunSetup.OpenInNotepad;
        _trayIconManager.QuitRequested += Shutdown;
        if (isFirstRun)
        {
            _trayIconManager.ShowFirstRunBalloon();
        }

        HotkeySettings hotkeySettings = HotkeySettings.Load();
        _hook = new LowLevelKeyboardHook(config.DoubleTapWindowMs, hotkeySettings);
        _hook.DoubleTapDetected += OnDoubleTapDetected;
        _hook.TriggerTapped += OnTriggerTapped;
        _hook.TriggerHoldStarted += OnTriggerHoldStarted;
        _hook.TriggerHoldEnded += OnTriggerHoldEnded;
        _hook.Install();

        // Signaled by a second launch's OnStartup above instead of it becoming its own instance —
        // brings the Hub forward rather than silently doing nothing, since that silence was exactly
        // what led to people relaunching the exe from Explorer and stacking up tray icons.
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateRegisteredWait = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => Dispatcher.BeginInvoke(() => OpenHub(history)),
            null,
            Timeout.Infinite,
            false);

        // Signaled by a newer launch asking this instance to step aside so it can take over (see
        // above). Quits the same way the tray's own "Quit" does, which releases the Mutex on the
        // way out for that waiting instance to pick up.
        _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        _quitRegisteredWait = ThreadPool.RegisterWaitForSingleObject(
            _quitEvent,
            (_, _) => Dispatcher.BeginInvoke(Shutdown),
            null,
            Timeout.Infinite,
            false);

        // Opened on every launch, not just lazily from the tray: per-user request, the Hub is now
        // the app's front door rather than a tray-only extra. Still torn down (not hidden) on
        // close — see OpenHub's own comment — so nothing about it stays resident once closed;
        // ShutdownMode="OnExplicitShutdown" (App.xaml) means closing it never quits the app.
        OpenHub(history);
    }

    private void OpenHub(DictationHistoryStore history)
    {
        if (_hubWindow is not null)
        {
            _hubWindow.Activate();
            return;
        }

        // Torn down rather than hidden on close (see CONTEXT.md's Hub definition: "Only exists
        // in memory while open"), so nothing about it is resident once the user closes it.
        _hubWindow = new HubWindow(history, _modeStore!, _envPath);
        _hubWindow.Closed += (_, _) => _hubWindow = null;
        _hubWindow.Show();
        _hubWindow.Activate();
    }

    private void OnDoubleTapDetected()
    {
        if (_trayIconManager?.IsPaused == true)
        {
            return;
        }

        // Hop through BeginInvoke even though we're already on the UI thread: it returns
        // control to the hook callback immediately so Windows never times it out, deferring
        // the actual recording and window work (which can take longer than the hook's budget)
        // to the next dispatcher cycle.
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => _overlay?.ToggleBarVisibility());
    }

    private void OnTriggerTapped()
    {
        if (_trayIconManager?.IsPaused == true)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => _overlay?.TriggerTap());
    }

    private void OnTriggerHoldStarted()
    {
        if (_trayIconManager?.IsPaused == true)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => _overlay?.StartHandsFreeRecording());
    }

    // Deliberately no pause check here, unlike OnTriggerTapped/OnTriggerHoldStarted above: if a
    // hold-to-talk Dictation is already in flight, releasing the key must always stop it, even if
    // Pause was toggled mid-hold. Leaving it running silently would be worse than one extra
    // Dictation slipping through a Pause that just took effect.
    private void OnTriggerHoldEnded() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => _overlay?.StopHandsFreeRecording());

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _transcriptionClient?.Dispose();
        _trayIconManager?.Dispose();
        _activateRegisteredWait?.Unregister(null);
        _quitRegisteredWait?.Unregister(null);
        _activateEvent?.Dispose();
        _quitEvent?.Dispose();

        // Explicit release (not just Dispose) so a newer instance waiting on this same Mutex wakes
        // up immediately with a clean handoff instead of an AbandonedMutexException. Only ever not
        // owned when this is a second-instance run that gave up and shut down without ever taking
        // ownership (the timeout path in OnStartup) — that case is expected, not a bug.
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
