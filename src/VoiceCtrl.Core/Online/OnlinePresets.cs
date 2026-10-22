namespace VoiceCtrl.Core.Online;

/// <summary>The Other provider as Settings keeps it. Not secret: the key is in the secrets store.</summary>
/// <param name="BaseUrl">The address before <c>/chat/completions</c>, e.g. <c>https://api.groq.com/openai/v1</c>.</param>
/// <param name="Model">The model id the provider expects.</param>
/// <param name="TextModel">An optional second model for text-only jobs (rewrite, email); empty uses <paramref name="Model"/>.</param>
/// <param name="Audio">What the key check found the model accepts.</param>
public sealed record OtherProviderSettings(string BaseUrl = "", string Model = "", AudioInput Audio = AudioInput.Unknown, string TextModel = "")
{
    /// <summary>An address and a model are both filled in. A method, so it is not saved or sent as a field.</summary>
    public bool IsComplete() => BaseUrl.Trim().Length > 0 && Model.Trim().Length > 0;
}

/// <summary>Everything a request to one provider needs but its key.</summary>
/// <param name="AudioModel">The model that hears a Dictation (or cleans its words, for a text-only model).</param>
/// <param name="AudioBackup">Tried in the same provider when <paramref name="AudioModel"/> is busy or
/// does not answer in time, before the next provider.</param>
/// <param name="TextModel">The model for text: a rewrite, an Email request's body, and the cleanup of a
/// two-step Dictation.</param>
/// <param name="TextBackup">The same for <paramref name="TextModel"/>.</param>
/// <param name="NoThink">Ask a reasoning model not to think first (<c>enable_thinking: false</c>).
/// Measured on NVIDIA: a short clip in about 1 s instead of 10.7 s.</param>
/// <param name="SendsTemperature">Sends <c>temperature: 0</c>. OpenAI's reasoning models refuse the field.</param>
/// <param name="TwoStep">The audio model only writes the words down and the text model cleans them,
/// rather than one model doing both in one call.</param>
/// <param name="TextReasoningEffort">Sent as <c>reasoning_effort</c> with the text models: the lowest
/// a reasoning model accepts, since a Dictation's latency matters more than depth.</param>
public sealed record OnlineEndpoint(
    OnlineProvider Provider,
    string BaseUrl,
    string AudioModel,
    string? AudioBackup,
    string TextModel,
    string? TextBackup,
    AudioInput Audio,
    bool NoThink = false,
    bool SendsTemperature = true,
    bool TwoStep = false,
    string? TextReasoningEffort = null)
{
    /// <summary><paramref name="model"/> is one of the text models, which may take a reasoning effort.</summary>
    public bool IsTextModel(string model) => model == TextModel || model == TextBackup;

    /// <summary>The model is sent the clip itself, as Gemini always is.</summary>
    public bool HearsAudio => Audio is AudioInput.AudioUrl or AudioInput.InputAudio;

    public string Name => OnlinePresets.NameOf(Provider);

    /// <summary>The models to try for a clip, primary first.</summary>
    public IReadOnlyList<string> AudioModels => AudioBackup is { Length: > 0 } backup && backup != AudioModel ? [AudioModel, backup] : [AudioModel];

    /// <summary>The models to try for text, primary first.</summary>
    public IReadOnlyList<string> TextModels => TextBackup is { Length: > 0 } backup && backup != TextModel ? [TextModel, backup] : [TextModel];
}

/// <summary>Where to get a provider's key, and what one looks like, for Setup and Settings.</summary>
public sealed record ProviderKeyInfo(string KeyPage, string KeyHint);

/// <summary>
/// The preset table (ticket 60): every built-in provider's address and model ids in one place, so a
/// model change is one line here. Each provider has a primary and a backup model per job; the backup
/// is tried before the next provider. Gemini keeps its own client and wire format; the advanced
/// settings can still override its primary model.
/// </summary>
public static class OnlinePresets
{
    // ---- Model ids (measured 2026-10-21; see the ticket 60 PR for the numbers) ----

    /// <summary>Gemini's primary: picked by the per-app, Style, email and Rewrite benches.</summary>
    public const string GeminiModel = "gemini-3.6-flash";

    /// <summary>Answered a short clip in about 1 s while the bigger flash models hung.</summary>
    public const string GeminiBackup = "gemini-3.5-flash-lite";

    /// <summary>NVIDIA's only model that hears audio, and only as an audio_url data URI.</summary>
    public const string NvidiaAudioModel = "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning";

    /// <summary>Cannot hear audio, but a strong, fast text model: 0.5-0.6 s with thinking off.</summary>
    public const string NvidiaTextModel = "nvidia/nemotron-3.5-super-vl-120b-a12b";

    /// <summary>The cleanest text of the NVIDIA screen, 0.5-2.1 s; covers super-vl's rare stall.</summary>
    public const string NvidiaTextBackup = "nvidia/nemotron-3-ultra-550b-a55b";

    /// <summary>From OpenAI's docs: takes input_audio in chat completions. Not checked with a live key.</summary>
    public const string OpenAiAudioModel = "gpt-audio";

    public const string OpenAiAudioBackup = "gpt-audio-mini";

    /// <summary>OpenAI's docs: "choose GPT-6.1 Sol to balance intelligence and cost".</summary>
    public const string OpenAiTextModel = "gpt-6.1-sol";

    public const string OpenAiTextBackup = "gpt-6-luna";

    private static readonly OnlineEndpoint Gemini = new(
        OnlineProvider.Gemini, "https://generativelanguage.googleapis.com/v1beta",
        GeminiModel, GeminiBackup, GeminiModel, GeminiBackup, AudioInput.InputAudio);

    private static readonly OnlineEndpoint Nvidia = new(
        OnlineProvider.Nvidia, "https://integrate.api.nvidia.com/v1",
        NvidiaAudioModel, null, NvidiaTextModel, NvidiaTextBackup, AudioInput.AudioUrl, NoThink: true);

    private static readonly OnlineEndpoint OpenAi = new(
        OnlineProvider.OpenAi, "https://api.openai.com/v1",
        OpenAiAudioModel, OpenAiAudioBackup, OpenAiTextModel, OpenAiTextBackup, AudioInput.InputAudio, SendsTemperature: false,
        // OpenAI's reasoning guide: "GPT-6.1 Sol does not support none or minimal", so low is the floor.
        TextReasoningEffort: "low");

    private static readonly Dictionary<OnlineProvider, ProviderKeyInfo> Keys = new()
    {
        [OnlineProvider.Gemini] = new("https://aistudio.google.com/apikey", "Starts with AIza"),
        [OnlineProvider.Nvidia] = new("https://build.nvidia.com/settings/api-keys", "Starts with nvapi-"),
        [OnlineProvider.OpenAi] = new("https://platform.openai.com/api-keys", "Starts with sk-"),
        [OnlineProvider.Other] = new("", "Your provider's API key"),
    };

    /// <summary>The order a fallback walks, and the pickers' order: the chosen provider first, then these with a saved key.</summary>
    public static readonly IReadOnlyList<OnlineProvider> FallbackOrder =
        [OnlineProvider.Gemini, OnlineProvider.Nvidia, OnlineProvider.OpenAi, OnlineProvider.Other];

    public static string NameOf(OnlineProvider provider) => provider switch
    {
        OnlineProvider.Gemini => "Gemini",
        OnlineProvider.Nvidia => "NVIDIA",
        OnlineProvider.OpenAi => "OpenAI",
        _ => "Other",
    };

    public static ProviderKeyInfo KeyInfoOf(OnlineProvider provider) => Keys[provider];

    /// <summary>The endpoint for <paramref name="provider"/>. <paramref name="geminiModel"/> is the
    /// primary model the advanced settings give Gemini, when they override it.</summary>
    public static OnlineEndpoint For(OnlineProvider provider, OtherProviderSettings other, string? geminiModel = null) => provider switch
    {
        OnlineProvider.Gemini => geminiModel is { Length: > 0 } model ? Gemini with { AudioModel = model, TextModel = model } : Gemini,
        OnlineProvider.Nvidia => Nvidia,
        OnlineProvider.OpenAi => OpenAi,
        _ => new(
            provider, other.BaseUrl.Trim().TrimEnd('/'), other.Model.Trim(), null,
            other.TextModel.Trim() is { Length: > 0 } text ? text : other.Model.Trim(), null, other.Audio),
    };
}
