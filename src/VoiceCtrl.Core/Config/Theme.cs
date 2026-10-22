namespace VoiceCtrl.Core.Config;

/// <summary>
/// The resolved, renderable Light-or-Dark value — what <see cref="ThemeResolver"/> computes from a
/// <see cref="ThemePreference"/> plus the live OS theme. Unlike <see cref="ThemePreference"/>, there
/// is no System member here: by the time a <see cref="Theme"/> exists, System has already been
/// resolved to one or the other.
/// </summary>
public enum Theme
{
    Light,
    Dark,
}
