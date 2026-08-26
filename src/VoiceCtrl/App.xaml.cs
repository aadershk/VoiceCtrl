using System.IO;
using System.Windows;
using System.Windows.Threading;
using VoiceCtrl.Bootstrap;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Injection;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using VoiceCtrl.Overlay;
using VoiceCtrl.Tray;

namespace VoiceCtrl;

public partial class App : Application
{
    private LowLevelKeyboardHook? _hook;
    private OverlayWindow? _overlay;
    private ITranscriptionClient? _transcriptionClient;
    private TranscriptionModeStore? _modeStore;
    private TrayIconManager? _trayIconManager;
    private string _envPath = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _envPath = Path.Combine(AppContext.BaseDirectory, ".env");
        bool isFirstRun = FirstRunSetup.Run(_envPath);
        StartMenuShortcut.EnsureExists();

        AppConfig config = ConfigLoader.Load(_envPath);
        _modeStore = TranscriptionModeStore.Load(config);

        // Creates the three user-editable files with commented starter content if they are not
        // there yet, so the tray entries below always open something that explains itself.
        PersonalizationStore personalization = PersonalizationStore.CreateDefault();
        var lastTranscription = new LastTranscriptionStore();

        var adaptiveClient = new AdaptiveTranscriptionClient(config, _modeStore, personalization);
        adaptiveClient.FellBackToOffline += () => _overlay?.ShowFellBackToOfflineNotice();
        _transcriptionClient = adaptiveClient;

        var textInjector = new ClipboardPasteInjector();
        _overlay = new OverlayWindow(config, _transcriptionClient, _modeStore, textInjector, lastTranscription);

        _trayIconManager = new TrayIconManager(_modeStore, lastTranscription,
        [
            new TrayFileEntry("Dictionary...", UserDataPaths.Dictionary),
            new TrayFileEntry("Snippets...", UserDataPaths.Snippets),
            new TrayFileEntry("App profiles...", UserDataPaths.Profiles),
        ]);
        _trayIconManager.SettingsRequested += () => FirstRunSetup.OpenInNotepad(_envPath);
        _trayIconManager.OpenFileRequested += FirstRunSetup.OpenInNotepad;
        _trayIconManager.QuitRequested += Shutdown;
        if (isFirstRun)
        {
            _trayIconManager.ShowFirstRunBalloon();
        }

        _hook = new LowLevelKeyboardHook(config.DoubleTapWindowMs);
        _hook.DoubleTapDetected += OnDoubleTapDetected;
        _hook.CancelRequested += OnCancelRequested;
        _hook.Install();
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

    private void OnCancelRequested()
    {
        // Fires for every Escape the user presses anywhere in Windows, so it exits on a single
        // bool read in the overwhelmingly common case where no dictation is running. The read is
        // safe without synchronisation: the hook callback runs on this same UI thread.
        if (_overlay?.IsRecording != true)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => _overlay?.CancelDictation());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _transcriptionClient?.Dispose();
        _trayIconManager?.Dispose();
        base.OnExit(e);
    }
}
