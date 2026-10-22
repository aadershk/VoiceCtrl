using System.IO;
using System.Text.Json;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// The kept Dictations (CONTEXT: Dictations, Retention) in %LocalAppData%\VoiceCtrl\history.json:
/// text, time and app, never audio. What is kept follows Keep Dictations: the last 50, the last
/// 24 hours, or nothing on disk (spec §5.3). The Dictations screen reads it; deleting a row can be
/// undone, Clear all can't.
/// <para>
/// Separate from <see cref="LastTranscriptionStore"/>, which holds the latest Transcription in
/// memory for Paste Again, so Paste Again still works when nothing is kept.
/// </para>
/// </summary>
public sealed class DictationHistoryStore
{
    /// <summary>How many Keep Dictations' "last 50" keeps.</summary>
    public const int MaxEntries = 50;

    private static readonly TimeSpan DayWindow = TimeSpan.FromHours(24);

    /// <summary>How many deleted rows Undo can still bring back. The toast's Undo only ever needs
    /// the latest; a few more cover deleting several rows in quick succession.</summary>
    private const int UndoCapacity = 10;

    private readonly string _filePath;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private readonly List<DictationRecord> _entriesOldestFirst;
    private readonly List<DictationRecord> _recentlyRemoved = [];
    private KeepDictations _keep;

    /// <summary>
    /// Raised after every change: a Dictation added, deleted, undone or cleared, or Keep Dictations
    /// pruning something. Raised outside the lock, on the thread that made the change (the UI
    /// thread, for every caller today).
    /// </summary>
    public event Action? Changed;

    private DictationHistoryStore(string filePath, List<DictationRecord> entriesOldestFirst, KeepDictations keep, TimeProvider clock)
    {
        _filePath = filePath;
        _entriesOldestFirst = entriesOldestFirst;
        _keep = keep;
        _clock = clock;

        // A shorter Keep Dictations chosen last run (or a clock that moved on) applies from the start.
        if (Prune())
        {
            Save();
        }
    }

    public static DictationHistoryStore Load(KeepDictations keep) => Load(UserDataPaths.History, keep, TimeProvider.System);

    /// <summary>Test seam: an in-memory store with no backing file.</summary>
    internal static DictationHistoryStore ForTesting(KeepDictations keep = KeepDictations.Last50, TimeProvider? clock = null) =>
        new(filePath: string.Empty, [], keep, clock ?? TimeProvider.System);

    internal static DictationHistoryStore Load(string filePath, KeepDictations keep, TimeProvider clock) =>
        new(filePath, ReadPersisted(filePath), keep, clock);

    /// <summary>Keep Dictations. A shorter choice prunes at once, on disk too.</summary>
    public KeepDictations Keep
    {
        get
        {
            lock (_gate)
            {
                return _keep;
            }
        }

        set
        {
            lock (_gate)
            {
                if (_keep == value)
                {
                    return;
                }

                _keep = value;
                if (!Prune())
                {
                    return;
                }

                Save();
            }

            Changed?.Invoke();
        }
    }

    /// <summary>How many kept Dictations choosing <paramref name="keep"/> would delete, so
    /// Settings can say so before it does.</summary>
    public int WouldRemove(KeepDictations keep)
    {
        lock (_gate)
        {
            return keep switch
            {
                KeepDictations.None => _entriesOldestFirst.Count,
                KeepDictations.Last24Hours => _entriesOldestFirst.Count(e => e.TimestampUtc < _clock.GetUtcNow().UtcDateTime - DayWindow),
                _ => Math.Max(0, _entriesOldestFirst.Count - MaxEntries),
            };
        }
    }

    /// <summary>Newest first, as the Dictations screen lists them. In "last 24 hours", anything
    /// that has aged out since the last change is dropped here.</summary>
    public IReadOnlyList<DictationRecord> Entries
    {
        get
        {
            lock (_gate)
            {
                if (Prune())
                {
                    Save();
                }

                return Enumerable.Reverse(_entriesOldestFirst).ToList();
            }
        }
    }

    /// <summary>Records a completed Dictation and saves at once, whether or not the Hub is open.
    /// Does nothing when Keep Dictations is None.</summary>
    /// <param name="original">The text before Smart cleanup edited it, or null when it did not.</param>
    public void Add(string text, string? appName = null, string? original = null)
    {
        lock (_gate)
        {
            if (_keep == KeepDictations.None)
            {
                return;
            }

            _entriesOldestFirst.Add(new DictationRecord
            {
                Text = text,
                TimestampUtc = _clock.GetUtcNow().UtcDateTime,
                AppName = appName,
                Original = original,
            });
            Prune();
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>Puts a Dictation's Original back as its text and drops the Original: the one-click
    /// restore after Smart cleanup. False when there is no such Dictation or nothing to restore.</summary>
    public bool RestoreOriginal(Guid id)
    {
        lock (_gate)
        {
            int index = _entriesOldestFirst.FindIndex(e => e.Id == id);
            if (index < 0 || _entriesOldestFirst[index].Original is not { } original)
            {
                return false;
            }

            DictationRecord record = _entriesOldestFirst[index];
            _entriesOldestFirst[index] = new DictationRecord
            {
                Id = record.Id,
                Text = original,
                TimestampUtc = record.TimestampUtc,
                AppName = record.AppName,
            };
            Save();
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Deletes one Dictation and saves. It stays in memory for <see cref="Undo"/>.</summary>
    public bool Remove(Guid id)
    {
        lock (_gate)
        {
            int index = _entriesOldestFirst.FindIndex(e => e.Id == id);
            if (index < 0)
            {
                return false;
            }

            _recentlyRemoved.Add(_entriesOldestFirst[index]);
            if (_recentlyRemoved.Count > UndoCapacity)
            {
                _recentlyRemoved.RemoveAt(0);
            }

            _entriesOldestFirst.RemoveAt(index);
            Save();
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>The delete toast's Undo: puts a deleted Dictation back where its time places it.
    /// False if it wasn't recently deleted, or Clear all has run since.</summary>
    public bool Undo(Guid id)
    {
        lock (_gate)
        {
            int removed = _recentlyRemoved.FindIndex(e => e.Id == id);
            if (removed < 0)
            {
                return false;
            }

            DictationRecord record = _recentlyRemoved[removed];
            _recentlyRemoved.RemoveAt(removed);
            int at = _entriesOldestFirst.FindIndex(e => e.TimestampUtc > record.TimestampUtc);
            _entriesOldestFirst.Insert(at < 0 ? _entriesOldestFirst.Count : at, record);
            Prune();
            Save();
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Clear all Dictations: removes every one from this PC. It can't be undone.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entriesOldestFirst.Clear();
            _recentlyRemoved.Clear();
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>Drops whatever Keep Dictations no longer keeps. Call under the lock; true if
    /// anything went, so the caller saves.</summary>
    private bool Prune()
    {
        int before = _entriesOldestFirst.Count;
        switch (_keep)
        {
            case KeepDictations.None:
                _entriesOldestFirst.Clear();
                _recentlyRemoved.Clear();
                break;

            case KeepDictations.Last24Hours:
                DateTime cutoff = _clock.GetUtcNow().UtcDateTime - DayWindow;
                _entriesOldestFirst.RemoveAll(e => e.TimestampUtc < cutoff);
                break;

            default:
                if (_entriesOldestFirst.Count > MaxEntries)
                {
                    _entriesOldestFirst.RemoveRange(0, _entriesOldestFirst.Count - MaxEntries);
                }

                break;
        }

        return _entriesOldestFirst.Count != before;
    }

    /// <summary>Written atomically, so a crash mid-save can't leave a half file that reads as
    /// corrupt and loses every Dictation. A failed write is logged, never thrown: the history
    /// must not break the Dictation that is being recorded.</summary>
    private void Save()
    {
        if (_filePath.Length == 0)
        {
            return;
        }

        try
        {
            AtomicFile.WriteAllBytes(_filePath, JsonSerializer.SerializeToUtf8Bytes(_entriesOldestFirst));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SimpleFileLogger.LogInfo($"Couldn't save history.json: {ex.Message}");
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

            // Oldest first on disk, but a restored backup or a hand edit could be out of order.
            return persisted?.OrderBy(e => e.TimestampUtc).ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        catch (JsonException ex)
        {
            // A corrupt history.json reads as empty rather than stopping startup; the next change
            // overwrites it.
            SimpleFileLogger.LogInfo($"history.json is not valid JSON, ignoring it: {ex.Message}");
            return [];
        }
    }
}
