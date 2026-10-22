namespace VoiceCtrl.Core.Online;

/// <summary>Who Online sends audio and text to (ticket 60). Gemini stays the default, for new installs
/// and existing ones alike (owner, 2026-10-21); this order is the pickers' order and the fallback's.</summary>
public enum OnlineProvider
{
    Gemini,
    Nvidia,
    OpenAi,

    /// <summary>Any address that speaks OpenAI's chat completions (Groq, OpenRouter, Mistral, a
    /// server on the user's network), with the model the user names.</summary>
    Other,
}

/// <summary>How a model is handed a clip in a chat completions request, which providers disagree on.</summary>
public enum AudioInput
{
    /// <summary>Not checked yet: an Other address whose key check could not reach it.</summary>
    Unknown,

    /// <summary><c>{"type":"audio_url","audio_url":{"url":"data:audio/wav;base64,…"}}</c>: NVIDIA's
    /// Nemotron omni hears only this, and silently ignores <see cref="InputAudio"/>.</summary>
    AudioUrl,

    /// <summary><c>{"type":"input_audio","input_audio":{"data":"…","format":"wav"}}</c>: OpenAI's shape.</summary>
    InputAudio,

    /// <summary>A text-only model: the PC's Offline speech model writes the words down and the cloud
    /// model does the cleanup.</summary>
    None,
}

