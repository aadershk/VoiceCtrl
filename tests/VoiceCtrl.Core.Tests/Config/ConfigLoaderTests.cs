using VoiceCtrl.Core.Config;
using Xunit;

namespace VoiceCtrl.Core.Tests.Config;

public class ConfigLoaderTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    [Fact]
    public void MissingFile_ReturnsAllDefaults()
    {
        AppConfig config = ConfigLoader.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        Assert.Equal(string.Empty, config.GeminiApiKey);
        Assert.Equal("gemini-3.7-flash", config.GeminiModelId);
        Assert.Equal(400, config.DoubleTapWindowMs);
        Assert.Equal(1500, config.AutoHideDelayMs);
        Assert.Equal("low", config.ThinkingLevel);
        Assert.Equal(string.Empty, config.StyleNotes);
        Assert.True(config.EnableToneAwareness);
        Assert.Equal("standard", config.CleanupLevel);
        Assert.Empty(config.PromptStyleApps);
        Assert.Empty(config.ChatStyleApps);
        Assert.Equal("Online", config.TranscriptionMode);
        Assert.Null(config.ExplicitTranscriptionMode);
    }

    [Fact]
    public void PromptAndChatStyleApps_ParsedAsCommaSeparatedLists()
    {
        string path = WriteEnvFile("""
            PROMPT_STYLE_APPS = claude, cursor
            CHAT_STYLE_APPS=whatsapp,telegram
            """);

        AppConfig config = ConfigLoader.Load(path);

        Assert.Equal(["claude", "cursor"], config.PromptStyleApps);
        Assert.Equal(["whatsapp", "telegram"], config.ChatStyleApps);
    }

    [Fact]
    public void ExplicitTranscriptionMode_OfflineIsTrackedSeparatelyFromDefault()
    {
        string path = WriteEnvFile("TRANSCRIPTION_MODE=offline");

        AppConfig config = ConfigLoader.Load(path);

        Assert.Equal("Offline", config.TranscriptionMode);
        Assert.Equal("Offline", config.ExplicitTranscriptionMode);
    }

    [Fact]
    public void UnrecognizedTranscriptionMode_FallsBackToDefaultButIsNotExplicit()
    {
        string path = WriteEnvFile("TRANSCRIPTION_MODE=sometimes");

        AppConfig config = ConfigLoader.Load(path);

        Assert.Equal("Online", config.TranscriptionMode);
        Assert.Null(config.ExplicitTranscriptionMode);
    }

    [Fact]
    public void AllKeysPresent_ParsesEachValue()
    {
        string path = WriteEnvFile("""
            GEMINI_API_KEY=test-key-123
            GEMINI_MODEL_ID=gemini-9.9-pro
            DOUBLE_TAP_WINDOW_MS=250
            AUTO_HIDE_DELAY_MS=3000
            THINKING_LEVEL=high
            DICTATION_STYLE_NOTES=Prefer "that" over "which"
            ENABLE_TONE_AWARENESS=false
            CLEANUP_LEVEL=aggressive
            """);

        AppConfig config = ConfigLoader.Load(path);

        Assert.Equal("test-key-123", config.GeminiApiKey);
        Assert.Equal("gemini-9.9-pro", config.GeminiModelId);
        Assert.Equal(250, config.DoubleTapWindowMs);
        Assert.Equal(3000, config.AutoHideDelayMs);
        Assert.Equal("high", config.ThinkingLevel);
        Assert.Equal("Prefer \"that\" over \"which\"", config.StyleNotes);
        Assert.False(config.EnableToneAwareness);
        Assert.Equal("aggressive", config.CleanupLevel);
    }

    [Fact]
    public void CommentsAndBlankLines_AreIgnored()
    {
        string path = WriteEnvFile("""

            # this is a comment
            GEMINI_API_KEY=test-key-123
            # GEMINI_MODEL_ID=commented-out-value

            """);

        AppConfig config = ConfigLoader.Load(path);

        Assert.Equal("test-key-123", config.GeminiApiKey);
        Assert.Equal("gemini-3.7-flash", config.GeminiModelId);
    }

    [Fact]
    public void LineWithoutEqualsSign_IsSkippedWithoutThrowing()
    {
        string path = WriteEnvFile("""
            this line has no separator
            GEMINI_API_KEY=test-key-123
            """);

        AppConfig config = ConfigLoader.Load(path);

        Assert.Equal("test-key-123", config.GeminiApiKey);
    }

    [Fact]
    public void NonIntegerNumericValue_FallsBackToDefault()
    {
        string path = WriteEnvFile("DOUBLE_TAP_WINDOW_MS=not-a-number");

        AppConfig config = ConfigLoader.Load(path);

        Assert.Equal(400, config.DoubleTapWindowMs);
    }

    [Fact]
    public void EmptyModelIdValue_FallsBackToDefault()
    {
        string path = WriteEnvFile("GEMINI_MODEL_ID=");

        AppConfig config = ConfigLoader.Load(path);

        Assert.Equal("gemini-3.7-flash", config.GeminiModelId);
    }

    [Fact]
    public void WhitespaceAroundKeyAndValue_IsTrimmed()
    {
        string path = WriteEnvFile("  GEMINI_API_KEY  =  test-key-123  ");

        AppConfig config = ConfigLoader.Load(path);

        Assert.Equal("test-key-123", config.GeminiApiKey);
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
