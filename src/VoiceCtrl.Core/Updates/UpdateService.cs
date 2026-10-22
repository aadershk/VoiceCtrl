using System.IO;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Updates;

/// <summary>A newer release the feed offered. The source keeps whatever else it needs.</summary>
public sealed record UpdateInfo(string Version);

/// <summary>A downloaded package failed its integrity check (spec §9): it is discarded.</summary>
public sealed class UpdateVerificationException(string message) : Exception(message);

/// <summary>
/// Where updates come from: Velopack on GitHub Releases in the app (slice 27), a fake before that
/// and in tests. <see cref="DownloadAsync"/> keeps a package only once it is verified, and throws
/// otherwise.
/// </summary>
public interface IUpdateSource
{
    /// <summary>A newer stable release, or null. Throws when the feed can't be reached.</summary>
    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads and verifies <paramref name="update"/>, reporting 0..1.</summary>
    Task DownloadAsync(UpdateInfo update, IProgress<double> progress, CancellationToken cancellationToken);

    /// <summary>Installs a downloaded update now and restarts VoiceCtrl.</summary>
    void ApplyAndRestart(UpdateInfo update);

    /// <summary>Deletes a downloaded update, so the next start doesn't install it.</summary>
    void Discard(UpdateInfo update);
}

/// <summary>The Automatic updates row's states (DESIGN §12). Available is Off's "a check that
/// finds a version offers Download and install".</summary>
public enum UpdateState
{
    UpToDate,
    Checking,
    Downloading,
    Ready,
    Off,
    Available,
}

/// <param name="NewVersion">The version being downloaded, ready, or offered.</param>
/// <param name="Progress">0..1 while Downloading.</param>
/// <param name="LastChecked">The last check that reached the feed, in Unix milliseconds.</param>
/// <param name="CheckFailed">The latest check couldn't reach the feed.</param>
public sealed record UpdateView(
    UpdateState State,
    bool Automatic,
    string CurrentVersion,
    string? NewVersion,
    double Progress,
    long? LastChecked,
    bool CheckFailed);

/// <summary>
/// The state machine behind Settings' Automatic updates row (spec §5.3): checks, background
/// downloads and Restart now, over an <see cref="IUpdateSource"/>. The host's timer calls
/// <see cref="ScheduledCheckAsync"/> (30 s after start, then every 6 h) and installs a downloaded
/// update at the next start; this answers the checks and says where things stand.
///
/// A downloaded package on disk always means the user wants it: it came from an automatic
/// download or from "Download and install", and turning Automatic updates off deletes it, so
/// "Off. You stay on 4.0.0" holds. <see cref="Changed"/> may fire on any thread.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly IUpdateSource _source;
    private readonly string _currentVersion;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();

    private bool _checking;
    private CancellationTokenSource? _download;
    private UpdateInfo? _downloading;
    private double _progress;
    private UpdateInfo? _offered;
    private UpdateInfo? _ready;
    private bool _checkFailed;

    public UpdateService(SettingsStore settings, IUpdateSource source, string currentVersion, Func<DateTimeOffset> clock)
    {
        _settings = settings;
        _source = source;
        _currentVersion = currentVersion;
        _clock = clock;
    }

    public event Action? Changed;

    public UpdateView View()
    {
        AppSection app = _settings.Current.App;
        lock (_gate)
        {
            (UpdateState state, UpdateInfo? shown) =
                _ready is not null ? (UpdateState.Ready, _ready)
                : _downloading is not null ? (UpdateState.Downloading, _downloading)
                : _checking ? (UpdateState.Checking, null)
                : _offered is not null ? (UpdateState.Available, _offered)
                : app.AutomaticUpdates ? (UpdateState.UpToDate, null)
                : (UpdateState.Off, (UpdateInfo?)null);

            return new UpdateView(
                state,
                app.AutomaticUpdates,
                _currentVersion,
                shown?.Version,
                state == UpdateState.Downloading ? _progress : 0,
                app.LastUpdateCheck?.ToUnixTimeMilliseconds(),
                _checkFailed);
        }
    }

    /// <summary>The timer's check: with Automatic updates off, nothing is checked until the user
    /// presses Check now.</summary>
    public Task ScheduledCheckAsync() =>
        _settings.Current.App.AutomaticUpdates ? CheckAsync() : Task.CompletedTask;

    /// <summary>Check now. With Automatic updates on, a found version downloads straight away;
    /// off, it is offered. Does nothing while a check or download is under way, or once an
    /// update is ready.</summary>
    public async Task CheckAsync()
    {
        lock (_gate)
        {
            if (_checking || _downloading is not null || _ready is not null)
            {
                return;
            }

            _checking = true;
            _checkFailed = false;
        }

        Raise();

        UpdateInfo? found = null;
        bool reached = false;
        try
        {
            found = await _source.CheckAsync(CancellationToken.None).ConfigureAwait(false);
            reached = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SimpleFileLogger.LogError("Update check", ex);
        }

        if (reached)
        {
            DateTimeOffset now = _clock();
            _settings.Update(s => s with { App = s.App with { LastUpdateCheck = now } });
        }

        // The user went back from this version after it failed to start (StartupMarker).
        if (found is not null && found.Version == _settings.Current.App.SkippedUpdateVersion)
        {
            found = null;
        }

        bool download;
        lock (_gate)
        {
            _checking = false;
            _checkFailed = !reached;
            _offered = found;
            download = found is not null && _settings.Current.App.AutomaticUpdates;
        }

        Raise();

        if (download)
        {
            await DownloadAsync().ConfigureAwait(false);
        }
    }

    /// <summary>"Download and install" on an offered version (or the automatic path after a
    /// check). A package that fails verification is dropped without a message; the next start
    /// tries again.</summary>
    public async Task DownloadAsync()
    {
        UpdateInfo update;
        CancellationTokenSource cancel;
        lock (_gate)
        {
            if (_offered is null || _downloading is not null || _ready is not null)
            {
                return;
            }

            update = _offered;
            _offered = null;
            _downloading = update;
            _progress = 0;
            cancel = _download = new CancellationTokenSource();
        }

        Raise();

        bool done = false;
        try
        {
            await _source.DownloadAsync(update, new ProgressSink(this), cancel.Token).ConfigureAwait(false);
            done = true;
        }
        catch (OperationCanceledException)
        {
            // Automatic updates were turned off mid-download.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SimpleFileLogger.LogError($"Update {update.Version} download", ex);
        }

        lock (_gate)
        {
            _downloading = null;
            _download = null;
            if (done)
            {
                _ready = update;
            }
        }

        cancel.Dispose();
        Raise();
    }

    /// <summary>The row's switch. Turning it off stops a download under way and deletes a
    /// package that is ready, so nothing installs at the next start.</summary>
    public void SetAutomatic(bool on)
    {
        _settings.Update(s => s with { App = s.App with { AutomaticUpdates = on } });
        UpdateInfo? discard = null;
        if (!on)
        {
            lock (_gate)
            {
                _download?.Cancel();
                discard = _ready;
                _ready = null;
            }
        }

        if (discard is not null)
        {
            try
            {
                _source.Discard(discard);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SimpleFileLogger.LogError($"Update {discard.Version} discard", ex);
            }
        }

        Raise();
    }

    /// <summary>Ready's Restart now: installs the update and restarts VoiceCtrl.</summary>
    public void RestartNow()
    {
        UpdateInfo? ready;
        lock (_gate)
        {
            ready = _ready;
        }

        if (ready is not null)
        {
            _source.ApplyAndRestart(ready);
        }
    }

    private void Report(double fraction)
    {
        lock (_gate)
        {
            if (_downloading is null)
            {
                return;
            }

            _progress = Math.Clamp(fraction, 0, 1);
        }

        Raise();
    }

    private void Raise() => Changed?.Invoke();

    public void Dispose()
    {
        lock (_gate)
        {
            _download?.Cancel();
        }
    }

    // Reports straight through: Progress<T> would post to whatever context created it.
    private sealed class ProgressSink(UpdateService owner) : IProgress<double>
    {
        public void Report(double value) => owner.Report(value);
    }
}
