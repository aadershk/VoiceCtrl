using System.IO;
using System.Text.Json;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// Holds the live <see cref="TranscriptionModePreference"/> and persists it to
/// %LocalAppData%\VoiceCtrl\prefs.json, deliberately a separate file from .env, which is never
/// programmatically rewritten (it holds the user's real Gemini API key).
/// </summary>
public sealed class TranscriptionModeStore
{
    private readonly string _filePath;

    /// <summary>The active preference. Plain get/set, same "mutable property the caller reads
    /// synchronously" shape as TrayIconManager.IsPaused, so callers that change it are responsible
    /// for calling <see cref="Save"/> afterward; this property alone does not persist.</summary>
    public TranscriptionModePreference Current { get; set; }

    private TranscriptionModeStore(string filePath, TranscriptionModePreference initial)
    {
        _filePath = filePath;
        Current = initial;
    }

    public static TranscriptionModeStore Load(AppConfig config) => Load(DefaultFilePath(), config);

    /// <summary>Test seam: an in-memory store with no backing file, for tests that only care about
    /// branching on <see cref="Current"/> and never call <see cref="Save"/>.</summary>
    internal static TranscriptionModeStore ForTesting(TranscriptionModePreference initial) =>
        new(filePath: string.Empty, initial);

    /// <summary>
    /// Reads the persisted preference, or, on first run of this feature before prefs.json exists,
    /// seeds and immediately persists one from <paramref name="config"/>. A user who explicitly set
    /// TRANSCRIPTION_MODE=Offline in .env may have done so for privacy (never send audio to Google),
    /// so that seeds to Offline rather than Auto; everything else seeds to Auto, which is safe since
    /// Auto behaves identically to Online whenever there's internet. Seeding is persisted right away
    /// so a later, unrelated .env edit can't silently change an already-established preference.
    /// </summary>
    internal static TranscriptionModeStore Load(string filePath, AppConfig config)
    {
        if (TryReadPersisted(filePath, out TranscriptionModePreference persisted))
        {
            return new TranscriptionModeStore(filePath, persisted);
        }

        TranscriptionModePreference seed = config.ExplicitTranscriptionMode == "Offline"
            ? TranscriptionModePreference.Offline
            : TranscriptionModePreference.Auto;

        var store = new TranscriptionModeStore(filePath, seed);
        store.Save();
        return store;
    }

    /// <summary>Best-effort: a failed write to LocalAppData (e.g. permissions) must never crash
    /// startup over a non-essential preference file; the in-memory <see cref="Current"/> still works
    /// for the rest of this run, it just won't survive a restart.</summary>
    public void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(new PrefsFile { TranscriptionMode = Current.ToString() }));
            SimpleFileLogger.LogInfo($"Transcription mode set to {Current}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool TryReadPersisted(string filePath, out TranscriptionModePreference mode)
    {
        mode = default;

        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            PrefsFile? prefs = JsonSerializer.Deserialize<PrefsFile>(File.ReadAllText(filePath));
            return prefs?.TranscriptionMode is not null &&
                   Enum.TryParse(prefs.TranscriptionMode, ignoreCase: true, out mode);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException)
        {
            // Corrupt prefs.json falls through to the same seed-and-persist path as a genuine first
            // run rather than crashing startup. Save() below then overwrites the corrupt file.
            return false;
        }
    }

    private static string DefaultFilePath() => UserDataPaths.Prefs;

    private sealed class PrefsFile
    {
        public string TranscriptionMode { get; set; } = nameof(TranscriptionModePreference.Auto);
    }
}
