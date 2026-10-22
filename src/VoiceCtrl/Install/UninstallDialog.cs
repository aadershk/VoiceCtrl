using System.Windows.Forms;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Install;

/// <summary>
/// "Also delete your data?" (spec §9): a native TaskDialog, since the uninstall hook runs without
/// WebView2 and must finish inside Velopack's time budget. The box starts
/// unticked, and a dialog nobody answers closes itself and keeps the data.
/// </summary>
internal static class UninstallDialog
{
    public const string DeleteCheckboxText = "Also delete your data";

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoMove = 0x0002;

    /// <summary>True only when the box was ticked and Continue pressed before the timeout.</summary>
    public static bool AskToDeleteData(TimeSpan timeout)
    {
        var continueButton = new TaskDialogButton("Continue");
        var deleteData = new TaskDialogVerificationCheckBox(DeleteCheckboxText, isChecked: false);
        var page = new TaskDialogPage
        {
            Caption = "VoiceCtrl",
            Heading = "Uninstalling VoiceCtrl",
            Text = "Your settings, API keys, Dictations, Dictionary, Snippets, Profiles, offline " +
                   "models and log stay on this PC, so reinstalling picks up where you left off.",
            Icon = TaskDialogIcon.Information,
            Verification = deleteData,
            Buttons = { continueButton },
            AllowCancel = true,
        };

        bool timedOut = false;
        using var timer = new System.Windows.Forms.Timer { Interval = (int)timeout.TotalMilliseconds };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timedOut = true;
            continueButton.PerformClick();
        };

        page.Created += (_, _) =>
        {
            // Velopack's own uninstall progress window is already up; this must sit above it.
            if (page.BoundDialog is { Handle: var handle } && handle != IntPtr.Zero)
            {
                NativeMethods.SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, NativeMethods.SWP_NOSIZE | SwpNoMove);
                Win32.SetForegroundWindow(handle);
            }

            timer.Start();
        };

        TaskDialogButton clicked = TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen);
        return clicked == continueButton && !timedOut && deleteData.Checked;
    }
}
