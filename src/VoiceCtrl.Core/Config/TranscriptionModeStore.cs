using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// The live <see cref="TranscriptionModePreference"/>, read from and saved to the
/// <see cref="SettingsStore"/>'s transcription section. Adds the one piece of behaviour Mode
/// changes need: <see cref="SwitchedToOfflineOrAuto"/>, for the offline model warm-up.
/// </summary>
public sealed class TranscriptionModeStore
{
    private readonly SettingsStore _settings;
    private TranscriptionModePreference _lastSeenMode;

    public TranscriptionModeStore(SettingsStore settings)
    {
        _settings = settings;
        _lastSeenMode = settings.Current.Transcription.Mode;
        settings.TranscriptionChanged += OnTranscriptionChanged;
    }

    public TranscriptionModePreference Current => _settings.Current.Transcription.Mode;

    /// <summary>Raised when the Mode changes to Offline or Auto, the two Modes that need the
    /// offline model resident, whoever changed it: this, the tray, or a direct settings update.</summary>
    public event Action? SwitchedToOfflineOrAuto;

    /// <summary>Test seam: a store over in-memory settings, for tests that only branch on
    /// <see cref="Current"/>.</summary>
    internal static TranscriptionModeStore ForTesting(TranscriptionModePreference initial) =>
        new(SettingsStore.InMemory(new UserSettings { Transcription = new TranscriptionSection { Mode = initial } }));

    /// <summary>Sets the Mode and saves it. The tray and the Hub both call this, so neither can
    /// drift from the other.</summary>
    public void SetAndSave(TranscriptionModePreference mode) =>
        _settings.Update(s => s with { Transcription = s.Transcription with { Mode = mode } });

    private void OnTranscriptionChanged(TranscriptionSection section)
    {
        if (section.Mode == _lastSeenMode)
        {
            return;
        }

        _lastSeenMode = section.Mode;
        SimpleFileLogger.LogInfo($"Transcription mode set to {section.Mode}");
        if (section.Mode is TranscriptionModePreference.Offline or TranscriptionModePreference.Auto)
        {
            SwitchedToOfflineOrAuto?.Invoke();
        }
    }
}
