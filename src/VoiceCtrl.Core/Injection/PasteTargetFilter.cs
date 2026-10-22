namespace VoiceCtrl.Core.Injection;

/// <summary>
/// Which foreground windows can be "the window that was in front before the Hub" (spec §7), the
/// one Paste Again from the Hub pastes into. Windows' own shell surfaces take the foreground on
/// the way to the Hub (clicking the tray icon activates the taskbar first; Alt+Tab and Start have
/// their own windows), and none of them is somewhere a user means to paste.
/// </summary>
public static class PasteTargetFilter
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",                         // the taskbar
        "Shell_SecondaryTrayWnd",                // the taskbar on another monitor
        "NotifyIconOverflowWindow",              // the tray's hidden-icons flyout (Windows 10)
        "TopLevelWindowForOverflowXamlIsland",   // the same flyout (Windows 11)
        "Progman",                               // the desktop
        "WorkerW",                               // the desktop, behind a wallpaper
        "Windows.UI.Core.CoreWindow",            // Start, Search, Action Center
        "XamlExplorerHostIslandWindow",          // Alt+Tab and Task View (Windows 11)
        "MultitaskingViewFrame",                 // Alt+Tab (Windows 10)
        "ForegroundStaging",                     // a transient window during app switches
    };

    /// <summary>True when a window of <paramref name="className"/> could take a paste.</summary>
    public static bool IsPasteTarget(string? className) =>
        !string.IsNullOrEmpty(className) && !ShellClasses.Contains(className);
}
