using System.IO;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Tests.Transcription;

/// <summary>
/// Runs the real language-pack models through <see cref="LocalTranscriptionClient"/>: the setup
/// each <see cref="OfflineModelKind"/> needs, and the switch from one pack to another while the
/// client stays alive. Opt-in, because the models are 0.7 to 1 GB each and are not in the repo: set
/// <c>VOICECTRL_TEST_MODELS</c> to a folder holding <c>parakeet-tdt-0.6b-v3</c> and
/// <c>whisper-turbo</c> (files under their local names, see <see cref="OfflineModels"/>) and
/// <c>VOICECTRL_TEST_CLIP</c> to a 16 kHz mono WAV that says "maintenance ... Chennai". Without
/// them each test returns at once.
/// </summary>
public class RealModelSmokeTests
{
    private static readonly string? ModelsRoot = Environment.GetEnvironmentVariable("VOICECTRL_TEST_MODELS");
    private static readonly string? ClipPath = Environment.GetEnvironmentVariable("VOICECTRL_TEST_CLIP");

    private static bool Enabled => ModelsRoot is not null && ClipPath is not null;

    [Theory]
    [InlineData("European")]
    [InlineData("AllLanguages")]
    public async Task ARealPack_TranscribesTheClip(string pack)
    {
        if (!Enabled)
        {
            return;
        }

        OfflineModel model = pack == "European" ? OfflineModels.European : OfflineModels.AllLanguages;
        using var client = new LocalTranscriptionClient(BuildConfig(), modelSource: () => model, modelsRoot: ModelsRoot);

        string? text = await client.TranscribeAsync(await File.ReadAllBytesAsync(ClipPath!));

        Assert.NotNull(text);
        Assert.Contains("maintenance", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chennai", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SwitchingPacks_UnloadsTheOldModel_AndTheNewOneWorks()
    {
        if (!Enabled)
        {
            return;
        }

        OfflineModel current = OfflineModels.European;
        using var client = new LocalTranscriptionClient(BuildConfig(), modelSource: () => current, modelsRoot: ModelsRoot);
        byte[] clip = await File.ReadAllBytesAsync(ClipPath!);

        string? first = await client.TranscribeAsync(clip);
        current = OfflineModels.AllLanguages;
        string? second = await client.TranscribeAsync(clip);
        await client.UnloadAsync();
        current = OfflineModels.European;
        string? third = await client.TranscribeAsync(clip);

        Assert.Contains("Chennai", first, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chennai", second, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chennai", third, StringComparison.OrdinalIgnoreCase);
    }

    private static AppConfig BuildConfig() => new()
    {
        GeminiApiKey = "",
        GeminiModelId = "gemini-test",
        DoubleTapWindowMs = 300,
        AutoHideDelayMs = 2000,
        ThinkingLevel = "low",
        StyleNotes = "",
        EnableToneAwareness = false,
        CleanupLevel = "standard",
        PromptStyleApps = [],
        ChatStyleApps = [],
        LocalModelVariant = OfflineModels.English.Directory,
        LocalNumThreads = 4,
        CompressUpload = false,
        SpokenPunctuation = false,
    };
}
