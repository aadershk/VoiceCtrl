using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Dictation;

/// <summary>
/// The kept Failed Dictations, newest first, capped at <see cref="Capacity"/>. The cap exists so a
/// second failure never silently drops the first one's audio, while a long outage can't pile up
/// unbounded audio in memory. Past it the oldest is dropped, and that is logged.
/// </summary>
internal sealed class FailedDictationList
{
    public const int Capacity = 5;

    private readonly List<FailedDictation> _newestFirst = [];

    public event Action? Changed;

    public IReadOnlyList<FailedDictation> Items => _newestFirst.ToList();

    public FailedDictation? Find(Guid id) => _newestFirst.Find(f => f.Id == id);

    public void Add(FailedDictation failed)
    {
        _newestFirst.Insert(0, failed);
        while (_newestFirst.Count > Capacity)
        {
            _newestFirst.RemoveAt(_newestFirst.Count - 1);
            SimpleFileLogger.LogInfo($"Dropped the oldest Failed Dictation (keeping {Capacity})");
        }

        Changed?.Invoke();
    }

    public void Remove(FailedDictation failed)
    {
        if (_newestFirst.Remove(failed))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>For a change made on an item in place (its reason, or whether it is retrying).</summary>
    public void RaiseChanged() => Changed?.Invoke();
}
