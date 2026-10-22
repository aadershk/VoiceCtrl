using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Config;
using Xunit;

namespace VoiceCtrl.Core.Tests.Config;

public class ConfigLoaderTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    [Fact]
    public void DefaultSettings_GiveTheShippedDefaults()
    {
        AppConfig config = ConfigLoader.Load(new UserSettings(), geminiApiKey: string.Empty);

        Assert.Equal(string.Empty, config.GeminiApiKey);
        Assert.False(config.IsApiKeyConfigured);
        Assert.Equal(OnlinePresets.GeminiModel, config.GeminiModelId);
        Assert.Equal(400, config.DoubleTapWindowMs);
        Assert.Equal(1500, config.AutoHideDelayMs);
        Assert.Equal("low", config.ThinkingLevel);
        Assert.Equal(string.Empty, config.StyleNotes);
        Assert.True(config.EnableToneAwareness);
        Assert.Equal("standard", config.CleanupLevel);
        Assert.Empty(config.PromptStyleApps);
        Assert.Empty(config.ChatStyleApps);
        Assert.Equal("parakeet-tdt-0.6b-v2", config.LocalModelVariant);
        Assert.False(config.CompressUpload);
        Assert.False(config.SpokenPunctuation);
    }

    [Fact]
    public void EveryAdvancedOverride_IsUsed()
    {
        var settings = new UserSettings
        {
            Transcription = new TranscriptionSection { DefaultCleanup = "aggressive" },
            Advanced = new AdvancedSection
            {
                GeminiModelId = "gemini-9.9-pro",
                DoubleTapWindowMs = 250,
                AutoHideDelayMs = 3000,
                ThinkingLevel = "high",
                StyleNotes = "Prefer \"that\" over \"which\"",
                EnableToneAwareness = false,
                PromptStyleApps = ["claude", "cursor"],
                ChatStyleApps = ["whatsapp"],
                LocalModelVariant = "some-other-model",
                LocalNumThreads = 4,
                CompressUpload = true,
                SpokenPunctuation = true,
            },
        };

        AppConfig config = ConfigLoader.Load(settings, geminiApiKey: "test-key-123");

        Assert.Equal("test-key-123", config.GeminiApiKey);
        Assert.True(config.IsApiKeyConfigured);
        Assert.Equal("gemini-9.9-pro", config.GeminiModelId);
        Assert.Equal(250, config.DoubleTapWindowMs);
        Assert.Equal(3000, config.AutoHideDelayMs);
        Assert.Equal("high", config.ThinkingLevel);
        Assert.Equal("Prefer \"that\" over \"which\"", config.StyleNotes);
        Assert.False(config.EnableToneAwareness);
        Assert.Equal("aggressive", config.CleanupLevel);
        Assert.Equal(["claude", "cursor"], config.PromptStyleApps);
        Assert.Equal(["whatsapp"], config.ChatStyleApps);
        Assert.Equal("some-other-model", config.LocalModelVariant);
        Assert.Equal(4, config.LocalNumThreads);
        Assert.True(config.CompressUpload);
        Assert.True(config.SpokenPunctuation);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(-3, 2)]
    [InlineData(64, 8)]
    public void LocalNumThreads_IsClamped(int configured, int expected)
    {
        var settings = new UserSettings { Advanced = new AdvancedSection { LocalNumThreads = configured } };

        Assert.Equal(expected, ConfigLoader.Load(settings, string.Empty).LocalNumThreads);
    }

    [Fact]
    public void BlankOverrides_FallBackToDefaults()
    {
        var settings = new UserSettings
        {
            Transcription = new TranscriptionSection { DefaultCleanup = " " },
            Advanced = new AdvancedSection { GeminiModelId = "", ThinkingLevel = " " },
        };

        AppConfig config = ConfigLoader.Load(settings, string.Empty);

        Assert.Equal(OnlinePresets.GeminiModel, config.GeminiModelId);
        Assert.Equal("low", config.ThinkingLevel);
        Assert.Equal("standard", config.CleanupLevel);
    }

    [Fact]
    public void ReadEnvFile_MissingFile_IsNull()
    {
        Assert.Null(ConfigLoader.ReadEnvFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
    }

    [Fact]
    public void ReadEnvFile_SkipsCommentsBlankLinesAndLinesWithoutASeparator()
    {
        string path = WriteEnvFile("""

            # this is a comment
            this line has no separator
            GEMINI_API_KEY=test-key-123
            # GEMINI_MODEL_ID=commented-out-value

            """);

        IReadOnlyDictionary<string, string> values = ConfigLoader.ReadEnvFile(path)!;

        Assert.Equal("test-key-123", Assert.Single(values).Value);
    }

    [Fact]
    public void ReadEnvFile_TrimsKeysAndValuesAndIgnoresKeyCase()
    {
        string path = WriteEnvFile("  gemini_api_key  =  test-key-123  ");

        IReadOnlyDictionary<string, string> values = ConfigLoader.ReadEnvFile(path)!;

        Assert.Equal("test-key-123", values["GEMINI_API_KEY"]);
    }

    [Fact]
    public void ReadEnvFile_KeepsEqualsSignsInsideTheValue()
    {
        string path = WriteEnvFile("DICTATION_STYLE_NOTES=a=b");

        Assert.Equal("a=b", ConfigLoader.ReadEnvFile(path)!["DICTATION_STYLE_NOTES"]);
    }

    private string WriteEnvFile(string contents)
    {
        string path = Path.Combine(Path.GetTempPath(), $"voicectrl-test-{Guid.NewGuid()}.env");
        File.WriteAllText(path, contents);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (string path in _tempFiles)
        {
            File.Delete(path);
        }
    }
}
