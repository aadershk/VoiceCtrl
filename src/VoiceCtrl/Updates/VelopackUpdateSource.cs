using Velopack;
using VoiceCtrl.Core.Logging;
using CoreUpdateInfo = VoiceCtrl.Core.Updates.UpdateInfo;
using IUpdateSource = VoiceCtrl.Core.Updates.IUpdateSource;
using static VoiceCtrl.Updates.UpdateFeed;

namespace VoiceCtrl.Updates;

/// <summary>
/// The Automatic updates row's source (spec §9): Velopack over GitHub Releases, with every
/// download checked against its signed manifest (<see cref="VerifiedPackages"/>). A copy that
/// Velopack didn't install (a dev build, the sandbox harness) never finds anything.
/// </summary>
internal sealed class VelopackUpdateSource(UpdateFeed feed, Action quit) : IUpdateSource
{
    private readonly PackageManager _manager = feed.Latest();
    private readonly object _gate = new();
    private UpdateInfo? _found;

    public async Task<CoreUpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        if (!_manager.IsInstalled)
        {
            return null;
        }

        UpdateInfo? found = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _found = found;
        }

        return found is null ? null : new CoreUpdateInfo(found.TargetFullRelease.Version.ToString());
    }

    public Task DownloadAsync(CoreUpdateInfo update, IProgress<double> progress, CancellationToken cancellationToken) =>
        VerifiedPackages.DownloadAsync(_manager, feed, Found(update), percent => progress.Report(percent / 100.0), cancellationToken);

    /// <summary>Hands the install to Velopack's updater, which waits for VoiceCtrl to quit, installs
    /// and starts it again, then quits the normal way (so the tray icon goes too).</summary>
    public void ApplyAndRestart(CoreUpdateInfo update)
    {
        VelopackAsset target = Found(update).TargetFullRelease;
        SimpleFileLogger.LogInfo($"Updates: Restart now to {target.Version}");
        _manager.WaitExitThenApplyUpdates(target, silent: true, restart: true, []);
        quit();
    }

    public void Discard(CoreUpdateInfo update) => VerifiedPackages.Discard(_manager, Found(update).TargetFullRelease);

    private UpdateInfo Found(CoreUpdateInfo update)
    {
        lock (_gate)
        {
            return _found is not null && _found.TargetFullRelease.Version.ToString() == update.Version
                ? _found
                : throw new InvalidOperationException($"{update.Version} wasn't found by the last check.");
        }
    }
}
