using System.Drawing;
using Microsoft.Win32;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Interop;

namespace VoiceCtrl.Web;

/// <summary>
/// What every page needs before its first paint: the effective Theme (the stored preference with
/// System resolved against Windows) and reduced motion (Windows' "Animation effects" off). Raises
/// <see cref="Changed"/> on the UI thread when either moves, from Settings or from Windows.
/// </summary>
internal sealed class SurfaceAppearance : IDisposable
{
    // DESIGN §2 --bg, so a window and a page that hasn't painted yet match (spec §3, no flash).
    private static readonly Color LightBackground = Color.FromArgb(0xF3, 0xED, 0xE4);
    private static readonly Color DarkBackground = Color.FromArgb(0x12, 0x13, 0x17);

    private readonly SettingsStore _settings;
    private readonly SynchronizationContext _ui;
    private readonly ThemeResolver _resolver = new();

    public SurfaceAppearance(SettingsStore settings, SynchronizationContext ui)
    {
        _settings = settings;
        _ui = ui;
        ReducedMotion = ReadReducedMotion();

        _settings.AppChanged += OnAppChanged;
        _resolver.OsThemeChanged += OnWindowsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Raised on the UI thread after <see cref="Theme"/>, <see cref="Preference"/> or
    /// <see cref="ReducedMotion"/> changed.</summary>
    public event Action? Changed;

    public ThemePreference Preference => _settings.Current.App.Theme;

    public Theme Theme => _resolver.Resolve(Preference);

    public bool ReducedMotion { get; private set; }

    public Color Background => Theme == Theme.Dark ? DarkBackground : LightBackground;

    public ThemeChanged ThemeEvent => new(Theme, Preference);

    /// <summary>
    /// Sets <c>data-theme</c> and <c>data-reduced-motion</c> on &lt;html&gt; before first paint. The
    /// script runs as the document is created, usually before &lt;html&gt; exists, so it waits for
    /// the element and then overrides the page's own defaults.
    /// </summary>
    public string DocumentCreatedScript =>
        "(function(){" +
        $"var t='{(Theme == Theme.Dark ? "dark" : "light")}',m='{(ReducedMotion ? "true" : "false")}';" +
        "function a(){var h=document.documentElement;h.dataset.theme=t;h.dataset.reducedMotion=m;}" +
        "if(document.documentElement){a();return;}" +
        "new MutationObserver(function(_,o){if(document.documentElement){a();o.disconnect();}})" +
        ".observe(document,{childList:true});})();";

    private static bool ReadReducedMotion() =>
        Win32.SystemParametersInfo(Win32.SPI_GETCLIENTAREAANIMATION, 0, out int animationsOn, 0) && animationsOn == 0;

    // Settings.Update runs on the UI thread (bridge handlers), but Post keeps every raise uniform.
    private void OnAppChanged(AppSection section) => _ui.Post(_ => Changed?.Invoke(), null);

    private void OnWindowsChanged() => _ui.Post(_ => Changed?.Invoke(), null);

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        bool reduced = ReadReducedMotion();
        _ui.Post(_ =>
        {
            if (reduced != ReducedMotion)
            {
                ReducedMotion = reduced;
                Changed?.Invoke();
            }
        }, null);
    }

    public void Dispose()
    {
        _settings.AppChanged -= OnAppChanged;
        _resolver.OsThemeChanged -= OnWindowsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _resolver.Dispose();
    }
}
