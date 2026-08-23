using System.Text.Json;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Personalization;

/// <summary>
/// Per-application overrides, keyed by process name.
///
/// This is an override layer rather than a replacement for the built-in tables. A user who edits
/// one entry should not silently freeze the other twelve at whatever they happened to be the day
/// the file was written, and improvements to the built-ins should still reach them. So an absent
/// application, or an absent field on a present application, falls through to the .env lists and
/// then to the built-in mapping, in that order.
/// </summary>
public sealed class AppProfileTable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppProfileTable Empty { get; } = new(new Dictionary<string, AppProfile>(StringComparer.OrdinalIgnoreCase));

    private readonly Dictionary<string, AppProfile> _profilesByProcessName;

    public int Count => _profilesByProcessName.Count;

    private AppProfileTable(Dictionary<string, AppProfile> profilesByProcessName) =>
        _profilesByProcessName = profilesByProcessName;

    /// <summary>
    /// Never throws. A malformed file is logged and treated as empty, because the alternative is
    /// an app that refuses to transcribe until the user fixes JSON they may not have written by
    /// hand, and the built-in behaviour underneath is a perfectly good place to land.
    /// </summary>
    public static AppProfileTable Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        // Read as raw elements rather than straight into AppProfile so that one entry of the wrong
        // shape costs only that entry. Deserializing the dictionary directly would abort the whole
        // file on the first bad value, which would take the user's other, valid, profiles with it.
        Dictionary<string, JsonElement>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            SimpleFileLogger.LogInfo($"profiles.json is not valid JSON, ignoring it: {ex.Message}");
            return Empty;
        }

        if (parsed is null)
        {
            return Empty;
        }

        var profiles = new Dictionary<string, AppProfile>(StringComparer.OrdinalIgnoreCase);
        foreach ((string processName, JsonElement element) in parsed)
        {
            string key = NormalizeProcessName(processName);
            if (key.Length == 0 || element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            try
            {
                if (element.Deserialize<AppProfile>(SerializerOptions) is { } profile)
                {
                    profiles[key] = profile;
                }
            }
            catch (JsonException ex)
            {
                SimpleFileLogger.LogInfo($"profiles.json entry \"{processName}\" is malformed, ignoring it: {ex.Message}");
            }
        }

        return new AppProfileTable(profiles);
    }

    public AppProfile? Resolve(string? processName) =>
        processName is not null && _profilesByProcessName.TryGetValue(NormalizeProcessName(processName), out AppProfile? profile)
            ? profile
            : null;

    /// <summary>Task Manager shows "slack.exe" while GetForegroundProcessName returns "slack", and
    /// the file is written by hand from whichever the user happened to be looking at.</summary>
    private static string NormalizeProcessName(string processName)
    {
        string trimmed = processName.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
    }

    /// <summary>
    /// Seeded with two entries that restate what the built-in tables already do for those apps, so
    /// writing the file changes nothing on its own and the schema is visible without documentation.
    /// </summary>
    public const string SeedContents = """
        {
          "_comment": [
            "Per-app dictation overrides, keyed by process name (with or without .exe).",
            "Every field is optional. Omit one to keep VoiceCtrl's built-in behaviour for it.",
            "tone:         free text, or \"none\" to suppress the built-in tone hint",
            "formatting:   \"structured\" (Markdown lists), \"prose\", or \"none\"",
            "cleanup:      \"light\", \"standard\" or \"aggressive\"",
            "instructions: anything else to tell the model when dictating into this app",
            "The two entries below match the built-in defaults. Edit or delete them freely."
          ],

          "slack": {
            "tone": "casual, conversational tone; contractions are fine",
            "formatting": "prose"
          },

          "code": {
            "tone": "likely a code comment or commit message; preserve technical terms and casing exactly as spoken",
            "formatting": "structured"
          }
        }
        """;
}
