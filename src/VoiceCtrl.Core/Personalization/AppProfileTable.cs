using System.Text.Json;
using System.Text.Json.Serialization;
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

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppProfileTable Empty { get; } = new(new Dictionary<string, AppProfile>(StringComparer.OrdinalIgnoreCase));

    private readonly Dictionary<string, AppProfile> _profilesByProcessName;

    public int Count => _profilesByProcessName.Count;

    /// <summary>Every entry, alphabetical by process name, for a GUI editor to list. Reading this
    /// rather than the file directly means a hand-edited file's own key order (or capitalization)
    /// never leaks into the Hub's display.</summary>
    public IReadOnlyList<KeyValuePair<string, AppProfile>> Entries =>
        _profilesByProcessName.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).ToList();

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
    /// the file is written by hand (or the Hub's Profile Editor) from whichever the user happened
    /// to be looking at. Public so the Profile Editor can normalize a new/edited process name the
    /// same way a reload of the saved file would, rather than drifting from it.</summary>
    public static string NormalizeProcessName(string processName)
    {
        string trimmed = processName.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed;
    }

    /// <summary>
    /// The file's "_comment" array, verbatim, or empty if the file has none or isn't valid JSON. A
    /// GUI editor that rewrites the file via <see cref="Format"/> carries this forward so the seed
    /// file's explanatory comment (or a user's own) survives edits made through the Hub rather than
    /// only through Notepad, mirroring <see cref="CustomDictionary.ExtractHeader"/> and
    /// <see cref="SnippetTable.ExtractHeader"/> for the other two personalization files.
    /// </summary>
    public static IReadOnlyList<string> ExtractComment(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            if (!document.RootElement.TryGetProperty("_comment", out JsonElement commentElement)
                || commentElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var lines = new List<string>();
            foreach (JsonElement item in commentElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    lines.Add(item.GetString() ?? string.Empty);
                }
            }

            return lines;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Renders <paramref name="comment"/> (from <see cref="ExtractComment"/>) as the file's
    /// leading "_comment" array, followed by one property per entry, the inverse of reading a
    /// file's comment then <see cref="Parse"/>-ing its entries. A profile field left unset (null)
    /// is omitted entirely rather than written as an explicit null, so it keeps falling through to
    /// built-in behavior on the next load exactly as it did before the edit.</summary>
    public static string Format(IReadOnlyList<string> comment, IReadOnlyList<KeyValuePair<string, AppProfile>> profiles)
    {
        var ordered = new Dictionary<string, object>(StringComparer.Ordinal);
        if (comment.Count > 0)
        {
            ordered["_comment"] = comment;
        }

        foreach (KeyValuePair<string, AppProfile> entry in profiles)
        {
            ordered[entry.Key] = entry.Value;
        }

        return JsonSerializer.Serialize(ordered, WriteOptions);
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
