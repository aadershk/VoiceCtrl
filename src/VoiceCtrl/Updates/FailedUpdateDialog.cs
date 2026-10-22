using System.Windows.Forms;
using Velopack;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Updates;
using static VoiceCtrl.Updates.UpdateFeed;

namespace VoiceCtrl.Updates;

/// <summary>
/// The failed-update guard's offer (spec §9): a native TaskDialog, since it comes up before
/// WebView2, which may be what broke. Going back is an ordinary verified download of the previous
/// release, installed straight away; the version left behind isn't offered again, though a later
/// fix is.
/// </summary>
internal static class FailedUpdateDialog
{
    /// <summary>Returns only when this version should carry on starting.</summary>
    public static void Offer(FailedUpdate failed, UpdateFeed feed, SettingsStore settings, string[] restartArgs)
    {
        var goBack = new TaskDialogButton($"Go back to {failed.From}") { AllowCloseDialog = false };
        var tryAgain = new TaskDialogButton($"Try {failed.To} again");
        var offer = new TaskDialogPage
        {
            Caption = "VoiceCtrl",
            Heading = $"VoiceCtrl {failed.To} didn't start last time",
            Text = $"Go back to {failed.From}, the version you had before? Your settings and Dictations stay as they are, " +
                   $"and VoiceCtrl won't offer {failed.To} again, only a later fix.",
            Icon = TaskDialogIcon.Warning,
            Buttons = { goBack, tryAgain },
            DefaultButton = goBack,
            AllowCancel = true,
        };

        var bar = new TaskDialogProgressBar(TaskDialogProgressBarState.Marquee) { Minimum = 0, Maximum = 100 };
        var downloading = new TaskDialogPage
        {
            Caption = "VoiceCtrl",
            Heading = $"Going back to {failed.From}",
            Text = "Downloading. VoiceCtrl starts again when it's done.",
            ProgressBar = bar,
            Buttons = { TaskDialogButton.Cancel },
        };

        var couldNot = new TaskDialogPage
        {
            Caption = "VoiceCtrl",
            Heading = $"Couldn't go back to {failed.From}",
            Text = $"The download didn't finish, so VoiceCtrl {failed.To} starts now.",
            Icon = TaskDialogIcon.Warning,
            Buttons = { TaskDialogButton.OK },
        };

        using var cancel = new CancellationTokenSource();
        downloading.Buttons[0].Click += (_, _) => cancel.Cancel();

        goBack.Click += async (_, _) =>
        {
            offer.Navigate(downloading);
            (PackageManager Manager, VelopackAsset Package)? previous = await DownloadAsync(failed, feed, cancel.Token, percent =>
            {
                bar.State = TaskDialogProgressBarState.Normal;
                bar.Value = percent;
            });

            if (previous is not { } found)
            {
                if (!cancel.IsCancellationRequested)
                {
                    downloading.Navigate(couldNot);
                }

                return;
            }

            settings.Update(s => s with { App = s.App with { SkippedUpdateVersion = failed.To } });
            SimpleFileLogger.LogInfo($"Updates: going back to {failed.From}");
            found.Manager.ApplyUpdatesAndRestart(found.Package, restartArgs);
        };

        TaskDialog.ShowDialog(offer, TaskDialogStartupLocation.CenterScreen);
    }

    private static async Task<(PackageManager, VelopackAsset)?> DownloadAsync(FailedUpdate failed, UpdateFeed feed, CancellationToken cancellationToken, Action<int> progress)
    {
        try
        {
            PackageManager pinned = feed.Pinned(failed.From);
            if (!pinned.IsInstalled || await pinned.CheckForUpdatesAsync() is not { } update)
            {
                SimpleFileLogger.LogInfo($"Updates: {failed.From} isn't in the feed");
                return null;
            }

            await VerifiedPackages.DownloadAsync(pinned, feed, update, progress, cancellationToken);
            return (pinned, update.TargetFullRelease);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SimpleFileLogger.LogError($"Updates: going back to {failed.From}", ex);
            return null;
        }
    }
}
