using System.Windows.Forms;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Updates;
using VoiceCtrl.Install;
using VoiceCtrl.Tray;
using VoiceCtrl.Updates;
using VoiceCtrl.Web;

namespace VoiceCtrl;

/// <summary>The entry point (spec §2).</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Before the installer hooks, since the uninstall hook shows a TaskDialog. Harmless when
        // a hook re-runs them.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Step 1: at install, update or uninstall, Velopack's hook runs here and the process exits.
        InstallerHooks.Run(args);

        // Installed now, so every await before Application.Run already returns to this thread.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        // Step 2: one instance per user. A second launch of this build opens the Hub and exits.
        using SingleInstance? instance = SingleInstance.Acquire();
        if (instance is null)
        {
            return 0;
        }

        // Step 3: settings first. The first v4 start runs the v3 migration, which reads the Run
        // key and the Start Menu shortcut, so this must come before anything rewrites either.
        (SettingsStore settings, SecretsStore secrets) = SettingsStartup.Open();
        SimpleFileLogger.LogInfo($"VoiceCtrl {AppCommands.Version} starting{(UserDataPaths.SandboxRoot is null ? "" : " (sandbox)")}");

        if (!WebViewRuntime.IsRuntimeInstalled())
        {
            SimpleFileLogger.LogInfo("WebView2 runtime not found");
            MessageBox.Show(WebViewRuntime.MissingRuntimeMessage, "VoiceCtrl", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        bool autostart = args.Contains(AutoStartManager.AutostartArgument, StringComparer.OrdinalIgnoreCase);

        // Before anything risky: install a waiting, verified update (which restarts VoiceCtrl), or
        // offer going back when the first start after an update died (spec §9). A restart keeps
        // --autostart, so a start at sign-in stays in the tray.
        var feed = new UpdateFeed();
        StartupMarker marker = UpdateStartup.Run(settings, feed, autostart ? [AutoStartManager.AutostartArgument] : []);

        // Step 7: the app lives until Quit.
        using var context = new VoiceCtrlContext(settings, secrets, instance, autostart, feed, marker);
        Application.Run(context);
        SimpleFileLogger.LogInfo("VoiceCtrl quit");
        return 0;
    }
}
