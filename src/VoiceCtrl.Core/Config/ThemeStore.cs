namespace VoiceCtrl.Core.Config;

/// <summary>
/// The live <see cref="ThemePreference"/>, read from and saved to the <see cref="SettingsStore"/>'s
/// app section. Subscribe to <see cref="SettingsStore.AppChanged"/> to follow changes.
/// </summary>
public sealed class ThemeStore(SettingsStore settings)
{
    public ThemePreference Current => settings.Current.App.Theme;

    /// <summary>Sets the Theme and saves it. Every call site changing Theme goes through here.</summary>
    public void SetAndSave(ThemePreference theme) =>
        settings.Update(s => s with { App = s.App with { Theme = theme } });
}
