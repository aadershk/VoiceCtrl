using Velopack;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Tray;

namespace VoiceCtrl.Install;

/// <summary>
/// Velopack's side of <c>Program.Main</c> (spec §9). Velopack starts the exe with a hook argument
/// at install, update and uninstall; <see cref="VelopackApp.Run"/> then runs the matching callback
/// and exits the process from inside, so nothing after it runs for a hook. A normal start passes
/// straight through.
///
/// The installer, not a hook, puts WebView2 in place (<c>vpk pack --framework webview2</c>): it
/// asks once, shows progress and has no time limit, where a hook is killed after 30 s. It also
/// owns the Start Menu shortcut (<c>--shortcuts StartMenuRoot</c>), which it writes before the
/// install hook runs, so the hook couldn't save v3's shortcut for the migration anyway; the Run
/// key comes first in that search (<see cref="V3Breadcrumbs"/>).
/// </summary>
internal static class InstallerHooks
{
    // Velopack 1.2.161 kills an uninstall hook after 60 s. The answer and the delete share that.
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(25);

    /// <summary>For WebView2 files the browser processes hold after Velopack stops the app. On
    /// a CI runner they have taken longer than 3 s to exit.</summary>
    private static readonly TimeSpan DeletePatience = TimeSpan.FromSeconds(20);

    // Velopack's own apply-on-start installs whatever package is waiting, checked or not. VoiceCtrl
    // installs a waiting update itself, once its signed manifest checks out (UpdateStartup).
    public static void Run(string[] args) =>
        VelopackApp.Build()
            .SetArgs(args)
            .SetAutoApplyOnStartup(false)
            .OnBeforeUninstallFastCallback(_ => BeforeUninstall())
            .Run();

    private static void BeforeUninstall()
    {
        // The sandbox harness never touches the real Run key (see VoiceCtrlContext).
        if (UserDataPaths.SandboxRoot is null)
        {
            try
            {
                AutoStartManager.Remove();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                SimpleFileLogger.LogError("Uninstall: Run key", ex);
            }
        }

        if (!UninstallDialog.AskToDeleteData(AnswerTimeout))
        {
            SimpleFileLogger.LogInfo("Uninstall: data kept");
            return;
        }

        // Nothing is logged after this: a log line would put the folder straight back.
        UserDataRemoval.DeleteAll(UserDataPaths.Root, DeletePatience);
    }
}
