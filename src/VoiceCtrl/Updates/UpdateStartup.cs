using Velopack;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Updates;
using VoiceCtrl.Web;
using static VoiceCtrl.Updates.UpdateFeed;

namespace VoiceCtrl.Updates;

/// <summary>
/// The update work at the start of <c>Program.Main</c> (spec §9), once settings are open and before
/// anything risky: install a downloaded, verified update; record this start for the update card and
/// the failed-update guard; and, if the first start after an update died, offer the previous version.
/// </summary>
internal static class UpdateStartup
{
    /// <summary>
    /// May not return: installing an update or going back restarts VoiceCtrl. Otherwise returns the
    /// marker to complete once the app is up.
    /// </summary>
    public static StartupMarker Run(SettingsStore settings, UpdateFeed feed, string[] restartArgs)
    {
        string current = AppCommands.Version;

        PackageManager manager = feed.Latest();
        if (VerifiedPackages.Pending(manager) is { } pending)
        {
            SimpleFileLogger.LogInfo($"Updates: installing {pending.Version}");
            manager.ApplyUpdatesAndRestart(pending, restartArgs);
        }

        var marker = new StartupMarker(UserDataPaths.StartupMarker);
        FailedUpdate? failed = marker.Begin(current, settings.Current.App.LastRunVersion);

        DateTimeOffset now = DateTimeOffset.Now;
        settings.Update(s => s with { App = UpdateCard.AtStart(s.App, current, now) });

        if (failed is not null)
        {
            SimpleFileLogger.LogInfo($"Updates: the first start of {failed.To} (from {failed.From}) didn't finish");
            FailedUpdateDialog.Offer(failed, feed, settings, restartArgs);
        }

        return marker;
    }
}
