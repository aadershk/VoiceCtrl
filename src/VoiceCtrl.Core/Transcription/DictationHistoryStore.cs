using System.IO;
using System.Text.Json;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Persists the text and timestamp of the last ~50 Dictations to
/// %LocalAppData%\VoiceCtrl\history.json — never audio, per CONTEXT.md's Dictations definition.
/// Deliberately a separate store from <see cref="LastTranscriptionStore"/>, which stays in-memory
/// only for the tray's single most-recent-transcript recovery; this one is what the Hub's Dictations
/// screen reads and searches.
/// </summary>
public sealed class DictationHistoryStore
{
    /// <summary>Bounds the file's growth: a recovery aid for the last few things said, not an
    /// unbounded transcript log.</summary>
    public const int MaxEntries = 50;

    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly List<DictationRecord> _entriesOldestFirst;

    /// <summary>
    /// Raised after a successful <see cref="Add"/> or <see cref="Remove"/>. The Hub's Dictations
    /// screen subscribes while open so a Dictation completed elsewhere (the Overlay) shows up
    /// immediately instead of only on the next time the Hub is opened fresh. Raised outside the
    /// lock, and on whatever thread called Add/Remove (today, always the single UI dispatcher
    /// thread both windows share), so a subscriber touching UI needs no extra marshalling.
    /// </summary>
    public event Action? Changed;

    private DictationHistoryStore(string filePath, List<DictationRecord> entriesOldestFirst)
    {
        _filePath = filePath;
        _entriesOldestFirst = entriesOldestFirst;
    }

    public static DictationHistoryStore Load() => Load(DefaultFilePath());

    /// <summary>Test seam: an in-memory store with no backing file.</summary>
    internal static DictationHistoryStore ForTesting() => new(filePath: string.Empty, []);

    internal static DictationHistoryStore Load(string filePath) => new(filePath, ReadPersisted(filePath));

    /// <summary>Newest first, since that is what a Dictations list shows at the top.</summary>
    public IReadOnlyList<DictationRecord> Entries
    {
        get
        {
            lock (_gate)
            {
                return Enumerable.Reverse(_entriesOldestFirst).ToList();
            }
        }
    }

    /// <summary>Entries whose text contains <paramref name="query"/>, newest first. A null or
    /// whitespace query returns every entry, so the Hub's search box can call this unconditionally
    /// instead of branching on whether the user has typed anything.</summary>
    public IReadOnlyList<DictationRecord> Search(string? query)
    {
        IReadOnlyList<DictationRecord> entries = Entries;
        if (string.IsNullOrWhiteSpace(query))
        {
            return entries;
        }

        return entries.Where(entry => entry.Text.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Removes one entry and persists immediately. Matched by reference — <paramref name="record"/>
    /// must be one of the instances handed out by <see cref="Entries"/> or <see cref="Search"/>,
    /// not a reconstructed copy, since DictationRecord has no id field of its own.
    /// </summary>
    public void Remove(DictationRecord record)
    {
        lock (_gate)
        {
            _entriesOldestFirst.Remove(record);
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Records a completed Dictation and persists it immediately. Called once per Dictation
    /// regardless of whether the Hub is open, since the point of a history is that it is already
    /// there the next time someone opens it.
    /// </summary>
    public void Add(string text)
    {
        lock (_gate)
        {
            _entriesOldestFirst.Add(new DictationRecord { Text = text, TimestampUtc = DateTime.UtcNow });

            while (_entriesOldestFirst.Count > MaxEntries)
            {
                _entriesOldestFirst.RemoveAt(0);
            }

            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>Best-effort, same reasoning as TranscriptionModeStore.Save: a failed write to
    /// LocalAppData must never crash the dictation pipeline over a non-essential history file.</summary>
    private void Save()
    {
        if (_filePath.Length == 0)
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(_entriesOldestFirst));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static List<DictationRecord> ReadPersisted(string filePath)
    {
        if (filePath.Length == 0 || !File.Exists(filePath))
        {
            return [];
        }

        try
        {
            List<DictationRecord>? persisted = JsonSerializer.Deserialize<List<DictationRecord>>(File.ReadAllText(filePath));
            return persisted ?? [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (JsonException ex)
        {
            // Corrupt history.json is treated as empty rather than crashing startup; the next Add
            // overwrites it with valid content.
            SimpleFileLogger.LogInfo($"history.json is not valid JSON, ignoring it: {ex.Message}");
            return [];
        }
    }

    private static string DefaultFilePath() => UserDataPaths.History;
}
