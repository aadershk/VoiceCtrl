using Microsoft.Win32;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// Computes the effective <see cref="Theme"/> (Light or Dark) from a <see cref="ThemePreference"/>
/// plus the live Windows theme — the same stored-preference-vs-resolved-runtime-value split the app
/// already has for transcription Mode (<see cref="TranscriptionModePreference.Auto"/> resolves
/// elsewhere; <see cref="ThemePreference.System"/> resolves here). Pure input/output beyond the OS
/// read itself: no WPF types anywhere in this class.
/// </summary>
public sealed class ThemeResolver : IDisposable
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string LightThemeValueName = "AppsUseLightTheme";

    private readonly bool _subscribedToSystemEvents;
    private bool _disposed;

    /// <summary>Whether Windows itself is currently set to a light apps theme. Kept current via
    /// <see cref="SystemEvents.UserPreferenceChanged"/> for the lifetime of this resolver.</summary>
    public bool IsOsLightTheme { get; private set; }

    /// <summary>Raised whenever <see cref="IsOsLightTheme"/> changes, so a subscriber holding a
    /// <see cref="ThemePreference.System"/> preference knows to re-resolve and re-theme without
    /// polling. Never raised for a test-seam instance, which never observes OS changes.</summary>
    public event Action? OsThemeChanged;

    /// <summary>Reads the live OS theme and subscribes to further changes. Callers own this
    /// instance's lifetime and should <see cref="Dispose"/> it when they stop caring about live
    /// updates (e.g. the Hub, only while open) — mirroring the app's existing lazy-load-on-open,
    /// dispose-on-close lifecycle elsewhere.</summary>
    public ThemeResolver() : this(ReadOsLightTheme(), subscribeToSystemEvents: true)
    {
    }

    /// <summary>Test seam: a resolver with a fixed OS theme and no SystemEvents subscription, so
    /// tests can exercise <see cref="Resolve"/> without touching the real registry or a message
    /// loop.</summary>
    internal static ThemeResolver ForTesting(bool isOsLightTheme) => new(isOsLightTheme, subscribeToSystemEvents: false);

    private ThemeResolver(bool isOsLightTheme, bool subscribeToSystemEvents)
    {
        IsOsLightTheme = isOsLightTheme;
        _subscribedToSystemEvents = subscribeToSystemEvents;

        if (subscribeToSystemEvents)
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
    }

    /// <summary>Explicit Light/Dark ignore OS state entirely; System resolves to whatever
    /// <see cref="IsOsLightTheme"/> currently says.</summary>
    public Theme Resolve(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => Theme.Light,
        ThemePreference.Dark => Theme.Dark,
        _ => IsOsLightTheme ? Theme.Light : Theme.Dark,
    };

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        bool isLight = ReadOsLightTheme();
        if (isLight == IsOsLightTheme)
        {
            return;
        }

        IsOsLightTheme = isLight;
        OsThemeChanged?.Invoke();
    }

    /// <summary>Windows itself defaults to light when the value is absent (pre-1607 builds, or a
    /// registry read failure), so an unreadable value resolves the same way Windows' own apps do.</summary>
    private static bool ReadOsLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            return key?.GetValue(LightThemeValueName) is not int value || value != 0;
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
