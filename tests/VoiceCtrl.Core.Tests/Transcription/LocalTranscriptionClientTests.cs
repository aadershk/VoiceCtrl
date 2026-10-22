using NAudio.Wave;
using SherpaOnnx;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public class LocalTranscriptionClientTests
{
    [Fact]
    public void PrewarmConnectionAsync_WhenModelAlreadyOnDisk_ConstructsRecognizerOffCallerThread()
    {
        using TestModelDirectory model = TestModelDirectory.CreateWithStubFiles();
        int? recognizerThreadId = null;
        int callerThreadId = -1;

        using var client = new LocalTranscriptionClient(
            BuildConfig(model.Variant),
            recognizerFactory: _ =>
            {
                recognizerThreadId = Environment.CurrentManagedThreadId;
                return null!;
            },
            modelsRoot: model.Root);

        // A dedicated Thread, not a pool thread, so its ManagedThreadId can never coincide with
        // whatever thread pool worker runs the factory - a pool-thread caller could flakily reuse
        // the same worker and produce a false pass. Blocking this thread (rather than awaiting) is
        // the point: it is not the test method's own thread, so it carries no deadlock risk.
#pragma warning disable xUnit1031
        var callerThread = new Thread(() =>
        {
            callerThreadId = Environment.CurrentManagedThreadId;
            client.PrewarmConnectionAsync().GetAwaiter().GetResult();
        });
#pragma warning restore xUnit1031
        callerThread.Start();
        callerThread.Join();

        Assert.NotNull(recognizerThreadId);
        Assert.NotEqual(callerThreadId, recognizerThreadId);
    }

    [Fact]
    public async Task ConcurrentPrewarmCalls_NeverConstructRecognizerConcurrently()
    {
        using TestModelDirectory model = TestModelDirectory.CreateWithStubFiles();
        int callersInsideFactory = 0;
        bool overlapDetected = false;
        using var firstEntered = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);

        using var client = new LocalTranscriptionClient(
            BuildConfig(model.Variant),
            recognizerFactory: _ =>
            {
                if (Interlocked.Increment(ref callersInsideFactory) > 1)
                {
                    overlapDetected = true;
                }

                firstEntered.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(10));
                Interlocked.Decrement(ref callersInsideFactory);
                return null!;
            },
            modelsRoot: model.Root);

        // First call's factory invocation blocks on releaseFirst, holding _recognizerLock the
        // whole time. The second call racing in behind it must wait for that lock rather than
        // running its own factory invocation concurrently - the "at most one construction inside
        // the factory at any instant" invariant asserted below.
        Task first = client.PrewarmConnectionAsync();
        Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(10)));

        Task second = client.PrewarmConnectionAsync();
        releaseFirst.Set();
        await Task.WhenAll(first, second);

        Assert.False(overlapDetected);
    }

    [Fact]
    public async Task PrewarmConnectionAsync_WhenConstructionThrows_SwallowsExceptionAsBestEffort()
    {
        using TestModelDirectory model = TestModelDirectory.CreateWithStubFiles();
        using var client = new LocalTranscriptionClient(
            BuildConfig(model.Variant),
            recognizerFactory: _ => throw new InvalidOperationException("model load failed"),
            modelsRoot: model.Root);

        await client.PrewarmConnectionAsync();
    }

    [Fact]
    public async Task TranscribeAsync_WhenConstructionThrows_PropagatesFailureToCaller()
    {
        using TestModelDirectory model = TestModelDirectory.CreateWithStubFiles();
        using var client = new LocalTranscriptionClient(
            BuildConfig(model.Variant),
            recognizerFactory: _ => throw new InvalidOperationException("model load failed"),
            modelsRoot: model.Root);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.TranscribeAsync(BuildSilentWavBytes()));
    }

    [Fact]
    public void AWhisperPack_IsConfiguredWithItsEncoderAndDecoder_AndPicksTheLanguageItself()
    {
        OfflineRecognizerConfig config = LocalTranscriptionClient.BuildRecognizerConfig(OfflineModels.AllLanguages, Path.Combine("m", "whisper-turbo"), numThreads: 3);

        Assert.Equal(Path.Combine("m", "whisper-turbo", "encoder.int8.onnx"), config.ModelConfig.Whisper.Encoder);
        Assert.Equal(Path.Combine("m", "whisper-turbo", "decoder.int8.onnx"), config.ModelConfig.Whisper.Decoder);
        Assert.Equal(Path.Combine("m", "whisper-turbo", "tokens.txt"), config.ModelConfig.Tokens);
        Assert.Equal("", config.ModelConfig.Whisper.Language);
        Assert.Equal("transcribe", config.ModelConfig.Whisper.Task);
        Assert.Equal(3, config.ModelConfig.NumThreads);
        Assert.Equal("cpu", config.ModelConfig.Provider);
    }

    [Fact]
    public void AParakeetModel_KeepsTheTransducerSetup()
    {
        OfflineRecognizerConfig config = LocalTranscriptionClient.BuildRecognizerConfig(OfflineModels.European, Path.Combine("m", "v3"), numThreads: 2);

        Assert.Equal("nemo_transducer", config.ModelConfig.ModelType);
        Assert.Equal(Path.Combine("m", "v3", "joiner.int8.onnx"), config.ModelConfig.Transducer.Joiner);
        Assert.Equal(Path.Combine("m", "v3", "encoder.int8.onnx"), config.ModelConfig.Transducer.Encoder);
    }

    [Fact]
    public async Task EachLoad_RunsTheModelTheSourceNamesAtThatMoment()
    {
        string root = Path.Combine(Path.GetTempPath(), $"voicectrl-test-models-{Guid.NewGuid():N}");
        try
        {
            foreach (OfflineModel model in new[] { OfflineModels.English, OfflineModels.European })
            {
                Directory.CreateDirectory(model.PathUnder(root));
                foreach (OfflineModelFile file in model.Files)
                {
                    File.WriteAllBytes(Path.Combine(model.PathUnder(root), file.Local), []);
                }
            }

            OfflineModel current = OfflineModels.English;
            var loaded = new List<string>();
            using var client = new LocalTranscriptionClient(
                BuildConfig("unused"),
                recognizerFactory: config =>
                {
                    loaded.Add(Path.GetFileName(Path.GetDirectoryName(config.ModelConfig.Tokens))!);
                    return null!;
                },
                modelSource: () => current,
                modelsRoot: root);

            await client.PrewarmConnectionAsync();
            current = OfflineModels.European;
            await client.PrewarmConnectionAsync();

            Assert.Equal([OfflineModels.English.Directory, OfflineModels.European.Directory], loaded);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EnglishWithADictionary_DecodesWithHotwordBias_AndPicksUpAChangedDictionary()
    {
        using TestModelDirectory model = TestModelDirectory.CreateWithStubFiles();
        string folder = Path.Combine(model.Root, "personal");
        Directory.CreateDirectory(folder);
        string dictionaryPath = Path.Combine(folder, "dictionary.txt");
        WriteDictionary(dictionaryPath, "Schiphol", "Kubernetes");

        var configs = new List<OfflineRecognizerConfig>();
        var hotwordLines = new List<string[]>();
        using var client = new LocalTranscriptionClient(
            BuildConfig(model.Variant),
            personalization: new PersonalizationStore(dictionaryPath, Path.Combine(folder, "s.txt"), Path.Combine(folder, "p.json")),
            recognizerFactory: config =>
            {
                configs.Add(config);
                hotwordLines.Add(File.Exists(config.HotwordsFile) ? File.ReadAllLines(config.HotwordsFile) : []);
                return null!;
            },
            modelsRoot: model.Root);

        await client.PrewarmConnectionAsync();
        Assert.Single(configs);
        Assert.Equal("modified_beam_search", configs[0].DecodingMethod);
        Assert.Equal(1.5f, configs[0].HotwordsScore);
        Assert.Equal("bpe", configs[0].ModelConfig.ModelingUnit);
        Assert.Equal(["Schiphol", "Kubernetes"], hotwordLines[0]);

        WriteDictionary(dictionaryPath, "Schiphol", "Kubernetes", "Aadersh");
        File.SetLastWriteTimeUtc(dictionaryPath, DateTime.UtcNow.AddMinutes(1));
        await client.PrewarmConnectionAsync();
        Assert.Equal(2, configs.Count);
        Assert.Equal(["Schiphol", "Kubernetes", "Aadersh"], hotwordLines[1]);
    }

    [Fact]
    public async Task EnglishWithoutADictionary_StaysGreedy()
    {
        using TestModelDirectory model = TestModelDirectory.CreateWithStubFiles();
        OfflineRecognizerConfig seen = default;
        using var client = new LocalTranscriptionClient(
            BuildConfig(model.Variant),
            recognizerFactory: config =>
            {
                seen = config;
                return null!;
            },
            modelsRoot: model.Root);

        await client.PrewarmConnectionAsync();

        Assert.Equal("greedy_search", seen.DecodingMethod);
        Assert.True(string.IsNullOrEmpty(seen.HotwordsFile));
    }

    [Fact]
    public async Task OtherPacksNeverGetHotwordBias_EvenWithADictionary()
    {
        string root = Path.Combine(Path.GetTempPath(), $"voicectrl-test-models-{Guid.NewGuid():N}");
        try
        {
            string folder = Path.Combine(root, "personal");
            Directory.CreateDirectory(folder);
            string dictionaryPath = Path.Combine(folder, "dictionary.txt");
            WriteDictionary(dictionaryPath, "Schiphol");

            foreach (OfflineModel other in new[] { OfflineModels.European, OfflineModels.AllLanguages })
            {
                Directory.CreateDirectory(other.PathUnder(root));
                foreach (OfflineModelFile file in other.Files)
                {
                    File.WriteAllBytes(Path.Combine(other.PathUnder(root), file.Local), []);
                }

                OfflineRecognizerConfig seen = default;
                using var client = new LocalTranscriptionClient(
                    BuildConfig("unused"),
                    personalization: new PersonalizationStore(dictionaryPath, Path.Combine(folder, "s.txt"), Path.Combine(folder, "p.json")),
                    recognizerFactory: config =>
                    {
                        seen = config;
                        return null!;
                    },
                    modelSource: () => other,
                    modelsRoot: root);

                await client.PrewarmConnectionAsync();
                Assert.Equal("greedy_search", seen.DecodingMethod);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void WriteDictionary(string path, params string[] words) =>
        File.WriteAllText(path, CustomDictionary.Format(string.Empty, words.Select(w => new DictionaryEntry(w, [])).ToList()));

    private static AppConfig BuildConfig(string localModelVariant) => new()
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
        LocalModelVariant = localModelVariant,
        LocalNumThreads = 1,
        CompressUpload = false,
        SpokenPunctuation = false,
    };

    private static byte[] BuildSilentWavBytes()
    {
        string tempPath = Path.GetTempFileName();
        try
        {
            using (var writer = new WaveFileWriter(tempPath, new WaveFormat(16000, 16, 1)))
            {
                byte[] silence = new byte[3200];
                writer.Write(silence, 0, silence.Length);
            }

            return File.ReadAllBytes(tempPath);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Stub files under a unique model variant name in a temp models root, so the client sees an
    /// already-downloaded model and skips the real download - the scenario the freeze bug actually
    /// happens in. Never under UserDataPaths.Models: on a clean machine that would create the data
    /// root, which the installer checks expect to be absent.
    /// </summary>
    private sealed class TestModelDirectory : IDisposable
    {
        private static readonly string[] ModelFileNames =
            ["encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt"];

        public string Root { get; }

        public string Variant { get; }

        private TestModelDirectory(string root, string variant)
        {
            Root = root;
            Variant = variant;
        }

        public static TestModelDirectory CreateWithStubFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), $"voicectrl-test-models-{Guid.NewGuid():N}");
            string variant = $"test-{Guid.NewGuid():N}";
            string path = Path.Combine(root, variant);
            Directory.CreateDirectory(path);
            foreach (string fileName in ModelFileNames)
            {
                File.WriteAllBytes(Path.Combine(path, fileName), []);
            }

            return new TestModelDirectory(root, variant);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
