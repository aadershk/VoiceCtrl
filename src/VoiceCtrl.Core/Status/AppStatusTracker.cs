using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Hotkey;

namespace VoiceCtrl.Core.Status;

/// <summary>
/// Derives the <see cref="AppStatus"/> from its sources (spec §7, Hub: "the status card derives
/// from Core state"): microphones, the API key's health, Mode and the Hotkey from settings, and
/// Pause, which lives here in memory only (DESIGN §12: it doesn't survive a restart).
/// <see cref="Changed"/> fires only when the status someone would see is different.
/// </summary>
public sealed class AppStatusTracker : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly IMicrophonePresence _microphones;
    private readonly ApiKeyHealth _keyHealth;
    private readonly object _gate = new();
    private AppStatus _current;
    private bool _paused;

    public AppStatusTracker(SettingsStore settings, IMicrophonePresence microphones, ApiKeyHealth keyHealth)
    {
        _settings = settings;
        _microphones = microphones;
        _keyHealth = keyHealth;
        _current = Resolve();

        _microphones.Changed += Refresh;
        _keyHealth.Changed += Refresh;
        _settings.TranscriptionChanged += OnTranscriptionChanged;
        _settings.DictationChanged += OnDictationChanged;
    }

    /// <summary>Raised with the new status, on the thread whose source changed. The host posts
    /// it on to the UI thread.</summary>
    public event Action<AppStatus>? Changed;

    public AppStatus Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Read by the input thread's gestures: while paused, the Hotkey and Trigger Key do nothing.</summary>
    public bool Paused
    {
        get
        {
            lock (_gate)
            {
                return _paused;
            }
        }
    }

    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            _paused = paused;
        }

        Refresh();
    }

    private void OnTranscriptionChanged(TranscriptionSection section) => Refresh();

    private void OnDictationChanged(DictationSection section) => Refresh();

    private void Refresh()
    {
        AppStatus next;
        lock (_gate)
        {
            next = Resolve();
            if (next == _current)
            {
                return;
            }

            _current = next;
        }

        Changed?.Invoke(next);
    }

    private AppStatus Resolve()
    {
        TranscriptionModePreference mode = _settings.Current.Transcription.Mode;
        HotkeySettings hotkeys = _settings.Current.Dictation.Hotkeys;
        string hotkey = hotkeys.HotkeyKey;

        // No microphone first: nothing can be dictated at all. A refused key only matters when
        // Online can be used.
        AppStatusKind kind = !_microphones.HasMicrophone ? AppStatusKind.NoMicrophone
            : _keyHealth.IsRejected && mode != TranscriptionModePreference.Offline ? AppStatusKind.ApiKeyNotWorking
            : _paused ? AppStatusKind.Paused
            : AppStatusKind.Ready;

        bool offlineFallback = kind == AppStatusKind.ApiKeyNotWorking && mode == TranscriptionModePreference.Auto;
        string provider = Online.OnlinePresets.NameOf(_settings.Current.Transcription.Provider ?? Online.ProviderMigration.Default);
        return new AppStatus(kind, hotkey, _paused, offlineFallback, hotkeys.IsTriggerReady ? hotkeys.TriggerChips : null, provider);
    }

    public void Dispose()
    {
        _microphones.Changed -= Refresh;
        _keyHealth.Changed -= Refresh;
        _settings.TranscriptionChanged -= OnTranscriptionChanged;
        _settings.DictationChanged -= OnDictationChanged;
    }
}
