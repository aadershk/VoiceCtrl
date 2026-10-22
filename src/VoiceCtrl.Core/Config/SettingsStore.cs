using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// The one home of every user-facing setting: %LocalAppData%\VoiceCtrl\settings.json (spec §5.2).
/// Holds the live <see cref="UserSettings"/>, writes it atomically on every change, and raises one
/// event per section that changed, which is how "Settings apply live" works: a consumer subscribes
/// to its section and rebinds.
/// <para>
/// Never refuses to start. A corrupt file is set aside and replaced with defaults; a file that
/// can't be read at all (locked, no access) is left alone, and this run keeps its changes in
/// memory rather than risk writing defaults over the user's real settings.
/// </para>
/// </summary>
public sealed class SettingsStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,

        // Integers are refused so a stray number can't become an enum value that has no name.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>Null when this run can't safely write the file; see the class summary.</summary>
    private readonly string? _filePath;
    private readonly object _gate = new();
    private volatile UserSettings _current;

    private SettingsStore(string? filePath, UserSettings initial)
    {
        _filePath = filePath;
        _current = initial;
    }

    /// <summary>The settings as of the last <see cref="Update"/>. Safe to read from any thread.</summary>
    public UserSettings Current => _current;

    public event Action<DictationSection>? DictationChanged;

    public event Action<TranscriptionSection>? TranscriptionChanged;

    public event Action<OverlaySection>? OverlayChanged;

    public event Action<PrivacySection>? PrivacyChanged;

    public event Action<AppSection>? AppChanged;

    public event Action<AdvancedSection>? AdvancedChanged;

    /// <summary>
    /// Loads <paramref name="filePath"/>, or, when it doesn't exist yet, calls
    /// <paramref name="seedWhenMissing"/> once (the v3 migration, in the app) and writes its result,
    /// so the next start reads the file and the seed never runs again.
    /// </summary>
    public static SettingsStore Open(string filePath, Func<UserSettings> seedWhenMissing)
    {
        if (!File.Exists(filePath))
        {
            var seeded = new SettingsStore(filePath, Normalize(seedWhenMissing()));
            seeded.Persist(seeded._current);
            return seeded;
        }

        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogError("settings.json could not be read; using defaults for this run without saving", ex);
            return new SettingsStore(filePath: null, DefaultsAfterLoss());
        }

        if (TryParse(json, out UserSettings? loaded))
        {
            if (loaded.Version > UserSettings.CurrentVersion)
            {
                SimpleFileLogger.LogInfo($"settings.json is version {loaded.Version}, newer than this build; reading the fields it knows");
            }

            return new SettingsStore(filePath, Normalize(loaded));
        }

        string? asidePath = AtomicFile.MoveAside(filePath, "corrupt");
        if (asidePath is null)
        {
            SimpleFileLogger.LogInfo("settings.json is corrupt and could not be set aside; using defaults for this run without saving");
            return new SettingsStore(filePath: null, DefaultsAfterLoss());
        }

        SimpleFileLogger.LogInfo($"settings.json was corrupt; kept it as {Path.GetFileName(asidePath)} and started from defaults");
        var recovered = new SettingsStore(filePath, DefaultsAfterLoss());
        recovered.Persist(recovered._current);
        return recovered;
    }

    /// <summary>A store with no backing file, for tests and in-process seeding.</summary>
    internal static SettingsStore InMemory(UserSettings initial) => new(filePath: null, Normalize(initial));

    /// <summary>
    /// Applies <paramref name="change"/> to the current settings, saves the result and raises the
    /// event of every section whose value changed, after <see cref="Current"/> already reflects it.
    /// Events run on the calling thread. A failed save is logged and never thrown: the change
    /// still applies for the rest of this run.
    /// </summary>
    public void Update(Func<UserSettings, UserSettings> change)
    {
        UserSettings before;
        UserSettings after;

        // The lock covers the write too, so two concurrent updates can't land on disk out of order.
        lock (_gate)
        {
            before = _current;
            after = Normalize(change(before) with { Version = UserSettings.CurrentVersion });
            if (Serialize(after) == Serialize(before))
            {
                return;
            }

            _current = after;
            Persist(after);
        }

        RaiseIfChanged(before.Dictation, after.Dictation, DictationChanged);
        RaiseIfChanged(before.Transcription, after.Transcription, TranscriptionChanged);
        RaiseIfChanged(before.Overlay, after.Overlay, OverlayChanged);
        RaiseIfChanged(before.Privacy, after.Privacy, PrivacyChanged);
        RaiseIfChanged(before.App, after.App, AppChanged);
        RaiseIfChanged(before.Advanced, after.Advanced, AdvancedChanged);
    }

    // Sections are compared by their JSON rather than record equality, because record equality
    // compares lists (AdvancedSection's app lists) by reference, and two equal lists would then
    // count as a change.
    private static void RaiseIfChanged<TSection>(TSection before, TSection after, Action<TSection>? handler)
    {
        if (handler is not null && Serialize(before) != Serialize(after))
        {
            handler(after);
        }
    }

    private void Persist(UserSettings settings)
    {
        if (_filePath is null)
        {
            return;
        }

        try
        {
            AtomicFile.WriteAllBytes(_filePath, JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogError("Could not save settings.json", ex);
        }
    }

    private static bool TryParse(string json, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out UserSettings? settings)
    {
        try
        {
            settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
            return settings is not null;
        }
        catch (JsonException)
        {
            settings = null;
            return false;
        }
    }

    // A settings.json existed, so this isn't a first run: sending the user back through Setup
    // would cost more than starting from defaults. Their key is in secrets.bin and unaffected.
    private static UserSettings DefaultsAfterLoss() =>
        new() { App = new AppSection { SetupComplete = true } };

    // A section written as null (hand-edited, or a future build's bug) would otherwise surface as a
    // null reference far from here. Missing sections never reach this: they keep their initializers.
    // A Paste Again chord Windows can't register would leave Paste Again silently dead.
    private static UserSettings Normalize(UserSettings settings) => settings with
    {
        Dictation = settings.Dictation is null
            ? new DictationSection()
            : settings.Dictation with
            {
                Hotkeys = settings.Dictation.Hotkeys ?? new(),
                PasteAgain = settings.Dictation.PasteAgain is { IsValid: true } pasteAgain ? pasteAgain : new(),
            },
        Transcription = settings.Transcription ?? new(),
        Overlay = settings.Overlay ?? new(),
        Privacy = settings.Privacy ?? new(),
        App = settings.App ?? new(),
        Style = settings.Style is null
            ? new()
            : settings.Style with
            {
                YourName = settings.Style.YourName ?? string.Empty,
                AppKinds = settings.Style.AppKinds ?? new Dictionary<string, Transcription.AppKind>(),
            },
        Advanced = settings.Advanced ?? new(),
    };

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
}
