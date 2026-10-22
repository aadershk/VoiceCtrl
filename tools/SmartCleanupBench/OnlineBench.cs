using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.EmailRequests;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Rewrite;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Tools.SmartCleanupBench;

/// <summary>
/// Which Online provider a bench runs against (ticket 60), from the options:
/// <c>--provider gemini|nvidia|openai</c> (Gemini when absent), <c>--model</c> to measure one
/// dictation model alone (no backup), <c>--text-model</c> likewise for text, and
/// <c>--two-step true</c> for "the audio model writes the words down, the text model cleans them".
/// The key comes from the environment (GEMINI_API_KEY, NVIDIA_API_KEY, OPENAI_API_KEY, process or
/// user scope) or VoiceCtrl's own secrets store. Never printed.
/// </summary>
internal sealed record OnlineBench(OnlineEndpoint Endpoint, string Key, bool SingleModel)
{
    public static OnlineBench From(IReadOnlyDictionary<string, string> options)
    {
        OnlineProvider provider = options.GetValueOrDefault("provider")?.ToLowerInvariant() switch
        {
            null or "gemini" => OnlineProvider.Gemini,
            "nvidia" => OnlineProvider.Nvidia,
            "openai" => OnlineProvider.OpenAi,
            string other => throw new ArgumentException($"Unknown provider {other}."),
        };

        OnlineEndpoint endpoint = OnlinePresets.For(provider, new OtherProviderSettings());
        bool single = false;
        if (options.TryGetValue("model", out string? model))
        {
            endpoint = endpoint with { AudioModel = model, AudioBackup = null };
            single = true;
        }

        if (options.TryGetValue("text-model", out string? textModel))
        {
            endpoint = endpoint with { TextModel = textModel, TextBackup = null };
        }

        if (options.GetValueOrDefault("two-step") == "true")
        {
            endpoint = endpoint with { TwoStep = true };
        }

        string variable = provider switch
        {
            OnlineProvider.Nvidia => "NVIDIA_API_KEY",
            OnlineProvider.OpenAi => "OPENAI_API_KEY",
            _ => "GEMINI_API_KEY",
        };
        string key = Environment.GetEnvironmentVariable(variable) is { Length: > 0 } fromProcess ? fromProcess
            : Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.User) is { Length: > 0 } fromUser ? fromUser
            : SecretsStore.Load(UserDataPaths.Secrets).KeyFor(provider);
        if (!AppConfig.IsUsableApiKey(key))
        {
            throw new InvalidOperationException($"No {endpoint.Name} key: set {variable} or save one in VoiceCtrl's Settings.");
        }

        return new OnlineBench(endpoint, key, single);
    }

    public string Describe =>
        $"Online ({Endpoint.Name}: {(Endpoint.TwoStep ? "two steps, " : "")}dictation {string.Join(" then ", Endpoint.AudioModels)}; " +
        $"text {string.Join(" then ", Endpoint.TextModels)})";

    public AppConfig Config(UserSettings? settings = null) =>
        ConfigLoader.Load(
            Endpoint.Provider == OnlineProvider.Gemini
                ? (settings ?? new UserSettings()) with { Advanced = new AdvancedSection { GeminiModelId = Endpoint.AudioModel } }
                : settings ?? new UserSettings(),
            Endpoint.Provider == OnlineProvider.Gemini ? Key : string.Empty);

    /// <summary>The Dictation client the app would use for this provider.</summary>
    public ITranscriptionClient Transcriber(
        Func<string?> app, Func<string?> title, Func<StyleSettings> styles) =>
        Endpoint.Provider == OnlineProvider.Gemini
            ? new GeminiTranscriptionClient(
                Config(), foregroundProcessNameProvider: app, apiKey: () => Key, cleanupLevel: () => CleanupLevels.Standard,
                foregroundWindowTitleProvider: title, styles: styles)
            {
                Models = SingleModel ? [Endpoint.AudioModel] : GeminiTranscriptionClient.ModelsFor(Endpoint.AudioModel),
            }
            : new CompatibleTranscriptionClient(
                Config(), () => Endpoint, () => Key, localSpeech: null, cleanupLevel: () => CleanupLevels.Standard,
                styles: styles, foregroundProcessName: app, foregroundWindowTitle: title);

    /// <summary>The rewrite client the app would use for this provider (it hears the instruction itself).</summary>
    public IRewriteClient Rewriter() =>
        Endpoint.Provider == OnlineProvider.Gemini
            ? new GeminiRewriteClient(Config(), null, () => Key)
            : new CompatibleRewriteClient(() => Endpoint, () => Key, null, null);

    /// <summary>The Email request body writer the app would use for this provider.</summary>
    internal IEmailBodyWriter EmailWriter() =>
        Endpoint.Provider == OnlineProvider.Gemini
            ? new GeminiEmailBodyWriter(Config(), () => Key)
            : new CompatibleEmailBodyWriter(() => Endpoint, () => Key);
}
