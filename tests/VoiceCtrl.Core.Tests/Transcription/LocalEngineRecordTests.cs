using System.IO;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

/// <summary>What the Offline client leaves in the engine record for one Dictation (VED-77).</summary>
public sealed class LocalEngineRecordTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"voicectrl-test-engine-client-{Guid.NewGuid():N}");

    private static readonly BuiltInVocabulary Vocabulary = new(
        ["Schiphol"], ["we", "land", "at", "on", "friday", "with", "the", "team", "book", "a", "table"]);

    private LocalTranscriptionClient NewClient(EngineRecorder recorder) => new(
        new AppConfig
        {
            GeminiApiKey = "",
            GeminiModelId = "gemini-test",
            DoubleTapWindowMs = 300,
            AutoHideDelayMs = 2000,
            ThinkingLevel = "low",
            StyleNotes = "",
            EnableToneAwareness = false,
            CleanupLevel = "light",
            PromptStyleApps = [],
            ChatStyleApps = [],
            LocalModelVariant = "test",
            LocalNumThreads = 1,
            CompressUpload = false,
            SpokenPunctuation = false,
        },
        foregroundProcessName: () => "notepad",
        builtInVocabulary: () => Vocabulary,
        engineRecorder: recorder);

    private EngineRecorder NewRecorder() => new(Path.Combine(_dir, "engine-record.json"), () => true);

    [Fact]
    public async Task ABuiltInRewrite_IsRecordedAsAPair_AndNoDictatedTextIsStored()
    {
        EngineRecorder recorder = NewRecorder();
        using LocalTranscriptionClient client = NewClient(recorder);

        string text = await client.TranscribeRecordedAsync("we land at Schipol on friday with Priya", CancellationToken.None);

        Assert.Contains("Schiphol", text);
        EngineEntry entry = Assert.Single(recorder.Entries);
        Assert.Equal([new WordPair("Schipol", "Schiphol")], entry.Vocabulary);
        Assert.Equal("skipped", entry.Mend);
        Assert.Equal("not available", entry.SmartCleanup);
        Assert.Empty(entry.GuardRejections);
        string stored = File.ReadAllText(Path.Combine(_dir, "engine-record.json"));
        Assert.DoesNotContain("Priya", stored);
        Assert.DoesNotContain("friday", stored, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("notepad", stored, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WithTheRecordOff_NothingIsWritten()
    {
        var recorder = new EngineRecorder(Path.Combine(_dir, "engine-record.json"), () => false);
        using LocalTranscriptionClient client = NewClient(recorder);

        await client.TranscribeRecordedAsync("we land at Schipol on friday", CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(_dir, "engine-record.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
