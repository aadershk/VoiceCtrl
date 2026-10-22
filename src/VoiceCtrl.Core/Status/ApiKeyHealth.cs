using VoiceCtrl.Core.Online;

namespace VoiceCtrl.Core.Status;

/// <summary>
/// Which Online providers refused their API key on the latest attempt (ticket 60: one key per
/// provider). The Online clients report every answer here; the status card reads the chosen
/// provider's ("NVIDIA key not working"), Auto Mode uses Offline while it's set, and a fallback
/// skips a provider whose key was refused. Held in memory only: a restart tries the keys again.
/// Thread-safe, since reports arrive from transcription continuations on the thread pool.
/// </summary>
public sealed class ApiKeyHealth
{
    private readonly object _gate = new();
    private readonly HashSet<OnlineProvider> _rejected = [];
    private readonly Func<OnlineProvider> _chosen;

    /// <param name="chosen">The provider Settings chose, read live; <see cref="IsRejected"/> and the
    /// overloads without a provider are about it. Defaults to Gemini, the one provider before ticket 60.</param>
    public ApiKeyHealth(Func<OnlineProvider>? chosen = null)
    {
        _chosen = chosen ?? (() => OnlineProvider.Gemini);
    }

    /// <summary>Raised after a provider's state changed, on the reporting thread.</summary>
    public event Action? Changed;

    /// <summary>The chosen provider refused its key.</summary>
    public bool IsRejected => IsRejectedFor(_chosen());

    public bool IsRejectedFor(OnlineProvider provider)
    {
        lock (_gate)
        {
            return _rejected.Contains(provider);
        }
    }

    public void ReportRejected() => ReportRejected(_chosen());

    /// <summary>The chosen provider answered, so its key works. Also the reset for a newly saved key.</summary>
    public void ReportAccepted() => ReportAccepted(_chosen());

    public void ReportRejected(OnlineProvider provider) => Set(provider, true);

    public void ReportAccepted(OnlineProvider provider) => Set(provider, false);

    /// <summary>Forgets every refusal: a key was saved or removed.</summary>
    public void Reset()
    {
        bool changed;
        lock (_gate)
        {
            changed = _rejected.Count > 0;
            _rejected.Clear();
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    private void Set(OnlineProvider provider, bool rejected)
    {
        lock (_gate)
        {
            bool changed = rejected ? _rejected.Add(provider) : _rejected.Remove(provider);
            if (!changed)
            {
                return;
            }
        }

        Changed?.Invoke();
    }
}
