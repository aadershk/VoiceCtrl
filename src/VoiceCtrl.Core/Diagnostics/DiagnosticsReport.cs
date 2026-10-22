using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Online;

namespace VoiceCtrl.Core.Diagnostics;

/// <summary>What the host knows for Copy diagnostics. Personalization arrives as counts only,
/// never its contents; the keys only as whether one is saved.</summary>
/// <param name="Online">The chosen Online provider's endpoint: its name and models, never a key.</param>
/// <param name="SavedProviderKeys">The Online providers with a key saved.</param>
public sealed record DiagnosticsFacts(
    string AppVersion,
    string OsDescription,
    string? WebView2Version,
    UserSettings Settings,
    OnlineEndpoint Online,
    IReadOnlyCollection<OnlineProvider> SavedProviderKeys,
    bool TypeSafeKeySaved,
    bool PasteAgainTaken,
    int DictionaryTerms,
    int Snippets,
    int Profiles,
    int KeptDictations,
    IReadOnlyList<string> LogLines,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string>? EngineRecordLines = null);

/// <summary>
/// The text Settings' Copy diagnostics puts on the clipboard for a GitHub issue (spec §5.3): the
/// app, Windows and WebView2 versions, the non-secret settings, Personalization counts and the
/// end of the log, every line through <see cref="LogScrubber"/>. Plain text in a fenced block, so
/// it pastes into an issue as it is.
/// </summary>
public static class DiagnosticsReport
{
    public const int LogLineCount = 500;

    // Settings that are the user's own words or a device's unique id: shown only as set or not.
    private static readonly HashSet<string> Withheld = new(StringComparer.Ordinal)
    {
        "advanced.styleNotes",
    };

    private static readonly HashSet<string> Omitted = new(StringComparer.Ordinal)
    {
        // The name is enough to tell which microphone (handoff from slice 20).
        "dictation.microphoneId",
    };

    public static string Build(DiagnosticsFacts facts, LogScrubber scrubber)
    {
        var report = new StringBuilder();
        report.AppendLine("```text");
        report.AppendLine($"VoiceCtrl {facts.AppVersion}");
        report.AppendLine(facts.OsDescription);
        report.AppendLine(facts.WebView2Version is null ? "WebView2 not found" : $"WebView2 {facts.WebView2Version}");
        report.AppendLine($"Report made {facts.CreatedAt.ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)}");

        report.AppendLine();
        report.AppendLine("## Settings");
        foreach ((string path, string value) in Flatten(facts.Settings))
        {
            report.AppendLine(scrubber.ScrubValue($"{path} = {value}"));
        }

        // Ticket 60: the provider and its models, so a report says who heard the Dictations.
        report.AppendLine($"Online provider: {facts.Online.Name} (Dictation {string.Join(", then ", facts.Online.AudioModels)}; " +
                          $"text {string.Join(", then ", facts.Online.TextModels)})");
        foreach (OnlineProvider provider in OnlinePresets.FallbackOrder)
        {
            report.AppendLine($"{OnlinePresets.NameOf(provider)} key: {Saved(facts.SavedProviderKeys.Contains(provider))}");
        }

        report.AppendLine($"TypeSafe key: {Saved(facts.TypeSafeKeySaved)}");
        if (facts.PasteAgainTaken)
        {
            report.AppendLine("Paste Again key: taken by another app");
        }

        report.AppendLine();
        report.AppendLine("## Personalization");
        report.AppendLine($"Dictionary words: {facts.DictionaryTerms}");
        report.AppendLine($"Snippets: {facts.Snippets}");
        report.AppendLine($"Profiles: {facts.Profiles}");
        report.AppendLine($"Kept Dictations: {facts.KeptDictations}");

        // Only while the engine record's switch is on (null otherwise). Its lines hold word pairs
        // and timings, never a Dictation; the pairs can still be a name, so they go through the
        // scrubber like a setting's value (keys, paths, e-mail addresses and the account name).
        if (facts.EngineRecordLines is { } engineLines)
        {
            report.AppendLine();
            report.AppendLine($"## Engine record ({engineLines.Count} Dictations)");
            if (engineLines.Count == 0)
            {
                report.AppendLine("Nothing recorded yet.");
            }

            foreach (string line in engineLines)
            {
                report.AppendLine(scrubber.ScrubValue(line));
            }
        }

        report.AppendLine();
        report.AppendLine($"## Log (last {LogLineCount} lines)");
        foreach (string line in facts.LogLines)
        {
            report.AppendLine(scrubber.Scrub(line));
        }

        report.AppendLine("```");
        return report.ToString();
    }

    /// <summary>The last <paramref name="count"/> lines of the log; none when it can't be read.</summary>
    public static IReadOnlyList<string> LastLines(string path, int count = LogLineCount)
    {
        try
        {
            // The log is small (one line per event), so reading it whole is fine; a queue keeps
            // only the tail in memory.
            var tail = new Queue<string>(count);
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            while (reader.ReadLine() is { } line)
            {
                if (tail.Count == count)
                {
                    tail.Dequeue();
                }

                tail.Enqueue(line);
            }

            return [.. tail];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Saved(bool saved) => saved ? "saved" : "not saved";

    /// <summary>The settings as they are saved, one <c>section.field = value</c> per leaf, so no
    /// value needs quotes (the scrubber's quote rule is for log lines).</summary>
    private static IEnumerable<(string Path, string Value)> Flatten(UserSettings settings)
    {
        JsonNode? root = JsonSerializer.SerializeToNode(settings, SettingsStore.JsonOptions);
        return root is null ? [] : Walk(root, prefix: "");
    }

    private static IEnumerable<(string Path, string Value)> Walk(JsonNode node, string prefix)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach ((string name, JsonNode? child) in obj)
                {
                    string path = prefix.Length == 0 ? name : $"{prefix}.{name}";
                    if (Omitted.Contains(path) || child is null)
                    {
                        continue;
                    }

                    if (Withheld.Contains(path))
                    {
                        yield return (path, "(set)");
                        continue;
                    }

                    foreach ((string Path, string Value) leaf in Walk(child, path))
                    {
                        yield return leaf;
                    }
                }

                break;

            case JsonArray array:
                yield return (prefix, string.Join(", ", array.Select(item => item?.ToString() ?? "")));
                break;

            default:
                yield return (prefix, node.ToString());
                break;
        }
    }
}
