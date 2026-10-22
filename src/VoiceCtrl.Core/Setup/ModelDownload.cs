using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Setup;

public enum ModelDownloadState
{
    /// <summary>Not running: not started yet, or stopped by Back or Skip. Bytes shows what is on disk.</summary>
    Idle,

    Downloading,

    Done,

    /// <summary>"The download stopped": the bar holds where it stopped, and Start resumes.</summary>
    Failed,
}

/// <param name="Bytes">On disk so far, across every model file.</param>
/// <param name="BytesPerSecond">This run's average, 0 until a second has passed.</param>
public sealed record ModelDownloadStatus(ModelDownloadState State, long Bytes, long TotalBytes, long BytesPerSecond);

/// <summary>
/// Setup's English model download (spec §7): start, stop, resume after a failure, with progress
/// thinned to about one change per MiB. The download itself is passed in, so the rules are tested
/// without a network. <see cref="Changed"/> is raised on whatever thread the download reports on;
/// the host marshals it to the page.
/// </summary>
public sealed class ModelDownload
{
    private const long ReportEvery = 1024 * 1024;

    private readonly Func<IProgress<long>, CancellationToken, Task> _download;
    private readonly Func<TimeSpan> _clock;
    private readonly long _totalBytes;
    private readonly object _gate = new();
    private ModelDownloadStatus _status;
    private CancellationTokenSource? _run;
    private long _runStartBytes;
    private TimeSpan _runStartTime;

    /// <param name="download">Downloads what is missing, reporting the bytes on disk.</param>
    /// <param name="isInstalled">True when every model file is on disk.</param>
    /// <param name="bytesOnDisk">What an earlier run left, finished and partial files together.</param>
    /// <param name="clock">A monotonic clock, for the rate.</param>
    public ModelDownload(
        Func<IProgress<long>, CancellationToken, Task> download,
        Func<bool> isInstalled,
        Func<long> bytesOnDisk,
        long totalBytes,
        Func<TimeSpan> clock)
    {
        _download = download;
        _clock = clock;
        _totalBytes = totalBytes;
        _status = isInstalled()
            ? new ModelDownloadStatus(ModelDownloadState.Done, totalBytes, totalBytes, 0)
            : new ModelDownloadStatus(ModelDownloadState.Idle, Math.Min(bytesOnDisk(), totalBytes), totalBytes, 0);
    }

    public event Action<ModelDownloadStatus>? Changed;

    public ModelDownloadStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <summary>Starts or resumes the download. Does nothing while it runs or once it is done.
    /// The task finishes when this run has settled, and never throws.</summary>
    public Task Start()
    {
        CancellationTokenSource run;
        ModelDownloadStatus started;
        lock (_gate)
        {
            if (_status.State is ModelDownloadState.Downloading or ModelDownloadState.Done)
            {
                return Task.CompletedTask;
            }

            run = new CancellationTokenSource();
            _run = run;
            _runStartBytes = _status.Bytes;
            _runStartTime = _clock();
            started = _status with { State = ModelDownloadState.Downloading, BytesPerSecond = 0 };
        }

        Set(run, started);
        return RunAsync(run);
    }

    /// <summary>Stops without a message (Back, Skip, or Setup closing). A partial file stays for the next start.</summary>
    public void Stop()
    {
        CancellationTokenSource? run;
        lock (_gate)
        {
            run = _run;
            if (run is null)
            {
                return;
            }

            _run = null;
            _status = _status with { State = ModelDownloadState.Idle, BytesPerSecond = 0 };
        }

        run.Cancel();
        Changed?.Invoke(Status);
    }

    private async Task RunAsync(CancellationTokenSource run)
    {
        ModelDownloadStatus final;
        try
        {
            await _download(new ProgressSink(this, run), run.Token).ConfigureAwait(false);
            final = new ModelDownloadStatus(ModelDownloadState.Done, _totalBytes, _totalBytes, 0);
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("Setup model download", ex);
            final = Status with { State = ModelDownloadState.Failed, BytesPerSecond = 0 };
        }

        lock (_gate)
        {
            if (_run == run)
            {
                _run = null;
            }
        }

        Set(run, final, requireCurrent: false);
        run.Dispose();
    }

    private void OnProgress(CancellationTokenSource run, long bytes)
    {
        ModelDownloadStatus next;
        lock (_gate)
        {
            if (_run != run || bytes - _status.Bytes < ReportEvery)
            {
                return;
            }

            double seconds = (_clock() - _runStartTime).TotalSeconds;
            long rate = seconds >= 1 ? (long)((bytes - _runStartBytes) / seconds) : 0;
            next = _status with { Bytes = Math.Min(bytes, _totalBytes), BytesPerSecond = rate };
        }

        Set(run, next);
    }

    /// <summary>Applies a status from <paramref name="run"/> unless a Stop or a newer run has
    /// taken over; <paramref name="requireCurrent"/> is false for the run's own final status,
    /// which lands only if nothing stopped it.</summary>
    private void Set(CancellationTokenSource run, ModelDownloadStatus status, bool requireCurrent = true)
    {
        lock (_gate)
        {
            bool current = _run == run || (!requireCurrent && _run is null && !run.IsCancellationRequested);
            if (!current)
            {
                return;
            }

            _status = status;
        }

        Changed?.Invoke(status);
    }

    private sealed class ProgressSink(ModelDownload owner, CancellationTokenSource run) : IProgress<long>
    {
        public void Report(long value) => owner.OnProgress(run, value);
    }
}
