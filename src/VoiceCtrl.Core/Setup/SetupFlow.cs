using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Setup;

/// <summary>Step 1's choice: how VoiceCtrl turns speech into text.</summary>
public enum SetupListening
{
    Online,
    Offline,
}

/// <summary>What the key check tells the page (research 13, step 2a).</summary>
public enum SetupKeyCheck
{
    /// <summary>"The key works": Setup moves on by itself.</summary>
    Works,

    /// <summary>"That key didn't work": the primary button becomes Use it anyway.</summary>
    DidNotWork,

    /// <summary>No internet, a timeout or an odd answer: the key is checked on the first
    /// Dictation instead, and Use it anyway is offered. Never blocks Setup.</summary>
    NotChecked,
}

/// <summary>What the user settled on when they pressed Open VoiceCtrl.</summary>
/// <param name="ApiKey">The key they continued with (checked or used anyway), or null when they
/// skipped it. Ignored for Offline.</param>
/// <param name="SmartCleanup">Whether the Smart cleanup model should be on this PC. Offline always
/// wants it; Online listeners choose, since their provider already tidies their text.</param>
/// <param name="Provider">The Online provider they chose (ticket 60); Gemini unless they picked another.</param>
/// <param name="Other">The address and model, when <paramref name="Provider"/> is Other, with what
/// the key check found the model hears.</param>
public sealed record SetupChoices(
    SetupListening Listening,
    string? ApiKey,
    bool StartWithWindows,
    bool SmartCleanup = true,
    OnlineProvider Provider = OnlineProvider.Gemini,
    OtherProviderSettings? Other = null);

/// <summary>
/// The rules for leaving first-run Setup (spec §7, research 13). The host applies Start with
/// Windows and opens the Hub after <see cref="Finish"/>; these only decide what is saved.
/// </summary>
public static class SetupFlow
{
    /// <summary>
    /// Saves the choices and marks Setup done. Online with a key saves Online and the chosen
    /// provider with its key. Online with the key skipped saves <b>Auto</b>, not Online: Auto falls
    /// back to Offline while no key is set, which is what the skip note promises, where Online with
    /// no key would fail every Dictation. The provider picked is kept either way, so Settings shows it.
    /// </summary>
    public static void Finish(SettingsStore settings, SecretsStore secrets, SetupChoices choices)
    {
        bool online = choices.Listening == SetupListening.Online;
        string key = online ? choices.ApiKey?.Trim() ?? "" : "";
        if (key.Length > 0)
        {
            secrets.SetKey(choices.Provider, key);
        }

        TranscriptionModePreference mode = choices.Listening switch
        {
            SetupListening.Offline => TranscriptionModePreference.Offline,
            _ when key.Length > 0 => TranscriptionModePreference.Online,
            _ => TranscriptionModePreference.Auto,
        };

        settings.Update(s => s with
        {
            Transcription = s.Transcription with
            {
                Mode = mode,
                SmartCleanup = choices.Listening == SetupListening.Offline || choices.SmartCleanup,
                Provider = online ? choices.Provider : s.Transcription.Provider,
                Other = online && choices.Provider == OnlineProvider.Other && choices.Other is { } other ? other : s.Transcription.Other,
            },
            App = s.App with { StartWithWindows = choices.StartWithWindows, SetupComplete = true },
        });
    }

    /// <summary>Closing Setup early (✕ or Alt+F4) counts as skipping: no key, Mode Auto, and
    /// Start with Windows left at its default. Setup doesn't come back.</summary>
    public static void Skip(SettingsStore settings) =>
        settings.Update(s => s with
        {
            Transcription = s.Transcription with { Mode = TranscriptionModePreference.Auto },
            App = s.App with { SetupComplete = true },
        });

    /// <summary>Only a rejection says the key didn't work; anything else (no internet, a timeout,
    /// a server error) leaves it unchecked, as v3 did.</summary>
    public static SetupKeyCheck ToKeyCheck(KeyValidation result) => result switch
    {
        KeyValidation.Valid => SetupKeyCheck.Works,
        KeyValidation.InvalidKey => SetupKeyCheck.DidNotWork,
        _ => SetupKeyCheck.NotChecked,
    };
}
