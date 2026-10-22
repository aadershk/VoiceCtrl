using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Online;

/// <summary>
/// Settles the Online provider for a settings file written before ticket 60 had one. Gemini, so an
/// install with a Gemini key keeps working exactly as before; Gemini is also a new install's default
/// (owner, 2026-10-21). Written to the file once, so a later change of default never moves anyone.
/// </summary>
public static class ProviderMigration
{
    public const OnlineProvider Default = OnlineProvider.Gemini;

    public static void Apply(SettingsStore settings, SecretsStore secrets)
    {
        if (settings.Current.Transcription.Provider is not null)
        {
            return;
        }

        OnlineProvider provider = ProviderFor(secrets);
        settings.Update(s => s with { Transcription = s.Transcription with { Provider = provider } });
        SimpleFileLogger.LogInfo($"Online provider settled as {OnlinePresets.NameOf(provider)}");
    }

    /// <summary>A saved Gemini key always means Gemini, whatever the default becomes.</summary>
    internal static OnlineProvider ProviderFor(SecretsStore secrets) =>
        AppConfig.IsUsableApiKey(secrets.KeyFor(OnlineProvider.Gemini)) ? OnlineProvider.Gemini : Default;
}
