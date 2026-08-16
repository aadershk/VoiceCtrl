using System.IO;
using System.Windows;
using System.Windows.Threading;
using VoiceCtrl.Bootstrap;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Hotkey;
using VoiceCtrl.Core.Injection;
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

        var adaptiveClient = new AdaptiveTranscriptionClient(config, _modeStore);
        adaptiveClient.FellBackToOffline += () => _overlay?.ShowFellBackToOfflineNotice();
        _transcriptionClient = adaptiveClient;

        var textInjector = new ClipboardPasteInjector();
        _overlay = new OverlayWindow(config, _transcriptionClient, _modeStore, textInjector);

        _trayIconManager = new TrayIconManager(_modeStore);
        _trayIconManager.SettingsRequested += () => FirstRunSetup.OpenInNotepad(_envPath);
        _trayIconManager.QuitRequested += Shutdown;
        if (isFirstRun)
        {
            _trayIconManager.ShowFirstRunBalloon();
        }

        _hook = new LowLevelKeyboardHook(config.DoubleTapWindowMs);
        _hook.DoubleTapDetected += OnDoubleTapDetected;
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
        // the actual window-showing work (which can take longer than the hook's budget) to
        // the next dispatcher cycle.
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => _overlay?.ToggleVisibility());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _transcriptionClient?.Dispose();
        _trayIconManager?.Dispose();
        base.OnExit(e);
    }
}
