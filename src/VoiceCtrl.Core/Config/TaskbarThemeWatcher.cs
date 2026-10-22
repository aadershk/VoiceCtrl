using Microsoft.Win32;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// Tracks whether Windows' taskbar specifically is light or dark, via the
/// <c>SystemUsesLightTheme</c> registry value — not <c>AppsUseLightTheme</c> (<see
/// cref="ThemeResolver"/>'s value), which Windows lets the user set independently of the taskbar
/// in Custom personalization mode. Reuses <see cref="ThemeResolver"/>'s exact mechanism (the same
/// <c>Personalize</c> key, the same <see cref="SystemEvents.UserPreferenceChanged"/> live-update
/// subscription) rather than sharing an instance with it, since the two watch different values for
/// different consumers (the tray icon asset here vs. the Hub/Setup palette there) that can change
/// independently of each other.
/// </summary>
public sealed class TaskbarThemeWatcher : IDisposable
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string LightTaskbarValueName = "SystemUsesLightTheme";

    private readonly bool _subscribedToSystemEvents;
    private bool _disposed;

    /// <summary>Whether Windows' taskbar is currently light. Kept current via
    /// <see cref="SystemEvents.UserPreferenceChanged"/> for the lifetime of this watcher.</summary>
    public bool IsTaskbarLightTheme { get; private set; }

    /// <summary>Raised whenever <see cref="IsTaskbarLightTheme"/> changes, so the tray icon can
    /// swap its light/dark-taskbar asset without polling.</summary>
    public event Action? TaskbarThemeChanged;

    /// <summary>Reads the live taskbar theme and subscribes to further changes. Owned by
    /// <c>TrayIconManager</c> for the tray icon's own whole-app-run lifetime (unlike
    /// <see cref="ThemeResolver"/>, which the Hub constructs and disposes per open/close), since
    /// the tray icon itself is always resident, not lazily shown.</summary>
    public TaskbarThemeWatcher() : this(ReadIsTaskbarLightTheme(), subscribeToSystemEvents: true)
    {
    }

    /// <summary>Test seam: a watcher with a fixed taskbar theme and no SystemEvents subscription,
    /// so tests can exercise <see cref="IsTaskbarLightTheme"/> without touching the real registry
    /// or a message loop.</summary>
    internal static TaskbarThemeWatcher ForTesting(bool isTaskbarLightTheme) =>
        new(isTaskbarLightTheme, subscribeToSystemEvents: false);

    private TaskbarThemeWatcher(bool isTaskbarLightTheme, bool subscribeToSystemEvents)
    {
        IsTaskbarLightTheme = isTaskbarLightTheme;
        _subscribedToSystemEvents = subscribeToSystemEvents;

        if (subscribeToSystemEvents)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        bool isLight = ReadIsTaskbarLightTheme();
        if (isLight == IsTaskbarLightTheme)
        {
            return;
        }

        IsTaskbarLightTheme = isLight;
        TaskbarThemeChanged?.Invoke();
    }

    /// <summary>Windows itself defaults to a light taskbar when the value is absent, mirroring
    /// <see cref="ThemeResolver"/>'s same default-on-unreadable-value behavior for
    /// AppsUseLightTheme.</summary>
    private static bool ReadIsTaskbarLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            return key?.GetValue(LightTaskbarValueName) is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_subscribedToSystemEvents)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
    }
}
