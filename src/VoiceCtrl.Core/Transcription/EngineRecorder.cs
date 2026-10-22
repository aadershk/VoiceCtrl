using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Collects what the engine does while one Dictation is transcribed (VED-77). Mutable and used by
/// one Dictation on one flow, so it needs no lock. Holds only words that were changed, never the
/// text around them.
/// </summary>
internal sealed class EngineTrace
{
    public string Mend { get; set; } = "skipped";

    public int MendProposed { get; set; }

    public int MendRefused { get; set; }

    public List<WordPair> MendApplied { get; } = [];

    public string SmartCleanup { get; private set; } = "not available";

    public List<string> GuardRejections { get; } = [];

    public List<WordPair> Vocabulary { get; } = [];

    public int ModelMs { get; set; }

    /// <summary>Takes in how the cleanup call ended and how long it took. Only a call that reached
    /// the model counts as model time: a gate or a missing model costs nothing worth reporting.</summary>
    public void RecordCleanup(SmartCleanupResult result, long elapsedMs)
    {
        string? reason = result.Reason;
        bool reachedModel = reason is null || !(reason.StartsWith("gate:", StringComparison.Ordinal) || reason == "no model");
        SmartCleanup = reason switch
        {
            null => "applied",
            _ when reason.StartsWith("gate:", StringComparison.Ordinal) => "gated off",
            "no model" => "not available",
            "timed out" => "timed out",
            _ when reason.StartsWith("model failed", StringComparison.Ordinal) => "failed",
            _ when reason.StartsWith("guard:", StringComparison.Ordinal) => "guard rejected",
            _ => "failed",
        };

        if (reason is not null && reason.StartsWith("guard:", StringComparison.Ordinal))
        {
            GuardRejections.Add(GuardKind(reason));
        }

        if (reachedModel)
        {
            ModelMs += (int)elapsedMs;
        }
    }

    public EngineEntry ToEntry(DateTimeOffset at, long totalMs) => new(
        at, Mend, MendProposed, MendRefused, [.. MendApplied], SmartCleanup, [.. GuardRejections], [.. Vocabulary], ModelMs, (int)totalMs);

    /// <summary>The kind of a guard refusal ("guard: invented word 'Priya'" gives "invented word").
    /// The guard's own reasons quote the dictated word, so only this fixed list reaches the record.</summary>
    internal static string GuardKind(string reason)
    {
        string text = reason.StartsWith("guard: ", StringComparison.Ordinal) ? reason["guard: ".Length..] : reason;
        (string Prefix, string Kind)[] kinds =
        [
            ("empty output", "empty output"),
            ("preamble", "preamble"),
            ("no words", "no words"),
            ("invented number", "invented number"),
            ("invented word", "invented word"),
            ("too many inserted words", "too many inserted words"),
            ("too many reworded words", "too many reworded words"),
            ("length ratio", "length ratio"),
            ("dropped", "dropped word"),
            ("word for word", "word for word"),
        ];

        foreach ((string prefix, string kind) in kinds)
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return "other";
    }
}

/// <summary>
/// The local, opt-in engine record (VED-77). Off unless the Settings switch is on; while on, keeps
/// the last <see cref="MaxEntries"/> Dictations' <see cref="EngineEntry"/> in one file under the
/// data root, written atomically. Turning the switch off deletes the file. Nothing leaves the PC.
/// </summary>
public sealed partial class EngineRecorder
{
    public const int MaxEntries = 500;

    private readonly string _path;
    private readonly Func<bool> _enabled;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private List<EngineEntry>? _entries;

    /// <param name="path">The record's file; <c>UserDataPaths.EngineRecord</c> unless a test says otherwise.</param>
    /// <param name="enabled">Asked at every use, so the Settings switch applies to the next Dictation.</param>
    public EngineRecorder(string path, Func<bool> enabled, Func<DateTimeOffset>? now = null)
    {
        _path = path;
        _enabled = enabled;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public bool Enabled => _enabled();

    /// <summary>A trace for the Dictation starting now, or null while the switch is off.</summary>
    internal EngineTrace? Begin() => Enabled ? new EngineTrace() : null;

    internal void Complete(EngineTrace trace, long totalMs) => Add(trace.ToEntry(_now(), totalMs));

    public void Add(EngineEntry entry)
    {
        if (!Enabled)
        {
            return;
        }

        EngineEntry clean = entry with
        {
            MendApplied = Sanitized(entry.MendApplied),
            Vocabulary = Sanitized(entry.Vocabulary),
        };

        lock (_gate)
        {
            List<EngineEntry> entries = LoadNoLock();
            entries.Add(clean);
            if (entries.Count > MaxEntries)
            {
                entries.RemoveRange(0, entries.Count - MaxEntries);
            }

            try
            {
                AtomicFile.WriteAllBytes(_path, JsonSerializer.SerializeToUtf8Bytes(entries));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SimpleFileLogger.LogError("Engine record", ex);
            }
        }
    }

    /// <summary>The recorded Dictations, oldest first.</summary>
    public IReadOnlyList<EngineEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. LoadNoLock()];
            }
        }
    }

    /// <summary>The recorded Dictations as lines for Copy diagnostics, oldest first.</summary>
    public IReadOnlyList<string> Lines() => [.. Entries.Select(entry => entry.ToLine())];

    /// <summary>Forgets every entry and deletes the file.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries = [];
            try
            {
                File.Delete(_path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SimpleFileLogger.LogError("Engine record", ex);
            }
        }
    }

    /// <summary>Deletes what is on disk when the switch is off, for a record left by an earlier run.</summary>
    public void ClearIfOff()
    {
        if (!Enabled)
        {
            Clear();
        }
    }

    private static List<WordPair> Sanitized(IReadOnlyList<WordPair> pairs) =>
        [.. pairs.Where(pair => SingleWord().IsMatch(pair.Wrong) && SingleWord().IsMatch(pair.Right))];

    private List<EngineEntry> LoadNoLock()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        try
        {
            _entries = File.Exists(_path) ? JsonSerializer.Deserialize<List<EngineEntry>>(File.ReadAllBytes(_path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable: start again; the next entry replaces the file.
            _entries = [];
        }

        return _entries;
    }

    // A mis-heard word and its fix: letters and apostrophes, one word of at most 40. Anything longer
    // or odder is dropped, so a sentence, an address or a path can never be written by mistake.
    [GeneratedRegex(@"^[\p{L}']{1,40}$")]
    private static partial Regex SingleWord();
}
