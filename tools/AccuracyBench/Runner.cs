using System.Diagnostics;
using System.Text.Json;
using NAudio.Wave;
using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Tools.AccuracyBench;

public sealed record ClipResult(string Id, string? Hypothesis, double LatencyMs, double AudioSeconds, string? Error);

public sealed record RunResult(string Pipeline, bool WithDictionary, DateTime StartedUtc, IReadOnlyList<ClipResult> Clips)
{
    public string Name => Pipeline + (WithDictionary ? "+dict" : string.Empty);
}

/// <summary>
/// Replays the recorded clips through one of the app's real transcription clients, the same classes
/// the app builds, so a number here is a number the app would produce. Pipelines: parakeet-v2
/// (English), parakeet-v3 (European), whisper-turbo (all languages), gemini (Online).
/// </summary>
public static class Runner
{
    public static readonly string[] Pipelines = ["parakeet-v2", "parakeet-v3", "whisper-turbo", "gemini"];

    /// <summary>VOICECTRL_BENCH_CONDITIONING = none | trim | gain | trim+gain (default: what the app ships, trim).</summary>
    private static SpeechConditioning Conditioning => Environment.GetEnvironmentVariable("VOICECTRL_BENCH_CONDITIONING") switch
    {
        "none" => SpeechConditioning.None,
        "trim" => SpeechConditioning.TrimSilence,
        "gain" => SpeechConditioning.NormalizeGain,
        "trim+gain" => SpeechConditioning.TrimSilence | SpeechConditioning.NormalizeGain,
        _ => SpeechConditioning.TrimSilence,
    };

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task<int> RunAsync(ScriptSet set, string pipeline, bool withDictionary)
    {
        if (!Pipelines.Contains(pipeline))
        {
            Console.Error.WriteLine("Unknown pipeline. Choose one of: " + string.Join(", ", Pipelines));
            return 2;
        }

        List<TestScript> scripts = set.Scripts.Where(s => File.Exists(set.AudioPath(s))).ToList();
        if (scripts.Count == 0)
        {
            Console.Error.WriteLine("No recorded clips under " + set.AudioDirectory + ". Run `record` first.");
            return 2;
        }

        PersonalizationStore personalization = withDictionary ? BuildBenchDictionary(set) : PersonalizationStore.Empty;
        string geminiKey = pipeline == "gemini" ? ReadGeminiKey() : string.Empty;
        if (pipeline == "gemini" && !AppConfig.IsUsableApiKey(geminiKey))
        {
            Console.Error.WriteLine("No Gemini key: set GEMINI_API_KEY or save one in VoiceCtrl's Settings.");
            return 2;
        }

        AppConfig config = ConfigLoader.Load(new UserSettings(), geminiKey);
        using ITranscriptionClient client = CreateClient(pipeline, config, personalization);

        Console.WriteLine($"{pipeline}{(withDictionary ? "+dict" : string.Empty)}: loading...");
        await client.PrewarmConnectionAsync();

        // One throwaway decode so the first scored clip is not charged for lazy native start-up.
        await TryTranscribeAsync(client, File.ReadAllBytes(set.AudioPath(scripts[0])));

        var clips = new List<ClipResult>();
        foreach (TestScript script in scripts)
        {
            byte[] wav = File.ReadAllBytes(set.AudioPath(script));
            double seconds = AudioSeconds(wav);

            var timer = Stopwatch.StartNew();
            (string? text, string? error) = await TryTranscribeAsync(client, wav);
            timer.Stop();

            clips.Add(new ClipResult(script.Id, text, timer.Elapsed.TotalMilliseconds, seconds, error));
            Console.WriteLine($"  {script.Id}  {timer.Elapsed.TotalSeconds,5:0.0} s for {seconds,4:0.0} s of audio{(error is null ? string.Empty : "  ERROR " + error)}");

            if (pipeline == "gemini")
            {
                await Task.Delay(1000);
            }
        }

        var result = new RunResult(pipeline, withDictionary, DateTime.UtcNow, clips);
        Directory.CreateDirectory(set.RunsDirectory);
        string label = Environment.GetEnvironmentVariable("VOICECTRL_BENCH_LABEL") is { Length: > 0 } l ? "-" + l : string.Empty;
        string file = Path.Combine(set.RunsDirectory, result.Name + label + ".json");
        File.WriteAllText(file, JsonSerializer.Serialize(result, Json));
        Console.WriteLine("Wrote " + file);
        return 0;
    }

    /// <summary>
    /// Re-applies the current Dictionary corrector to the saved raw transcripts of a no-dictionary
    /// run, with no audio and no model: the quick loop for tuning the corrector. Prints the scored
    /// row beside the saved +dict run; it saves nothing, so the baseline stays the baseline.
    /// </summary>
    public static int Replay(ScriptSet set, string pipeline)
    {
        RunResult? raw = LoadRuns(set).FirstOrDefault(r => r.Pipeline == pipeline && !r.WithDictionary);
        if (raw is null)
        {
            Console.Error.WriteLine($"No saved {pipeline} run to replay. Run `run {pipeline}` first.");
            return 2;
        }

        // VOICECTRL_BENCH_DICT=none replays with no personal Dictionary (the out-of-the-box case);
        // VOICECTRL_BENCH_BUILTIN=1 adds the built-in vocabulary (ticket 36b) after it.
        bool noDictionary = Environment.GetEnvironmentVariable("VOICECTRL_BENCH_DICT") == "none";
        bool builtIn = Environment.GetEnvironmentVariable("VOICECTRL_BENCH_BUILTIN") == "1";
        PersonalizationStore personalization = BuildBenchDictionary(set);
        IReadOnlyList<DictionaryEntry> dictionary = noDictionary ? [] : personalization.Dictionary;
        IReadOnlyCollection<string> personalWords = dictionary.Select(entry => entry.Word).ToList();
        if (double.TryParse(Environment.GetEnvironmentVariable("VOICECTRL_BENCH_POP"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double share))
        {
            BuiltInVocabulary.RewriteShare = share;
        }

        var loadClock = Stopwatch.StartNew();
        BuiltInVocabulary vocabulary = builtIn ? BuiltInVocabulary.Shared : BuiltInVocabulary.Empty;
        if (builtIn)
        {
            Console.WriteLine($"[builtin] {vocabulary.TermCount} terms, loaded and indexed in {loadClock.ElapsedMilliseconds} ms, {GC.GetTotalMemory(true) / 1_000_000} MB managed heap");
        }


        var corrected = new List<ClipResult>();
        foreach (ClipResult clip in raw.Clips)
        {
            if (clip.Hypothesis is null)
            {
                corrected.Add(clip);
                continue;
            }

            string personal = DictionaryCorrector.Apply(clip.Hypothesis, dictionary);
            string final = vocabulary.Apply(personal, personalWords);
            if (builtIn && final != personal)
            {
                Console.WriteLine($"[builtin] {clip.Id}: {personal}{Environment.NewLine}          -> {final}");
            }

            corrected.Add(clip with { Hypothesis = final });
        }

        var replayed = new RunResult(pipeline + "-replay" + (builtIn ? "+builtin" : "") + (noDictionary ? "-nodict" : ""), !noDictionary, DateTime.UtcNow, corrected);
        var saved = LoadRuns(set).Where(r => r.Pipeline == pipeline).ToList();
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "VoiceCtrlBench", "replay-" + pipeline + ".json"), JsonSerializer.Serialize(replayed, Json));
        Console.WriteLine(Scorer.Report(set, [.. saved, replayed]));
        return 0;
    }

    public static IReadOnlyList<RunResult> LoadRuns(ScriptSet set) =>
        Directory.Exists(set.RunsDirectory)
            ? Directory.GetFiles(set.RunsDirectory, "*.json").Order()
                .Select(f => JsonSerializer.Deserialize<RunResult>(File.ReadAllText(f))!).ToList()
            : [];

    private static ITranscriptionClient CreateClient(string pipeline, AppConfig config, PersonalizationStore personalization)
    {
        if (pipeline == "gemini")
        {
            // No foreground app: the harness measures speech, not the per-app formatting hints.
            return new GeminiTranscriptionClient(config, foregroundProcessNameProvider: () => null, personalization: personalization);
        }

        OfflineModel model = pipeline switch
        {
            "parakeet-v3" => OfflineModels.European,
            "whisper-turbo" => OfflineModels.AllLanguages,
            _ => OfflineModels.English,
        };

        // The owner's installed English model is reused as is. Anything else downloads into a
        // separate folder, so running the harness never changes which packs the app thinks are installed.
        string installed = model.PathUnder(UserDataPaths.Models);
        string modelsRoot = Directory.Exists(installed)
            ? UserDataPaths.Models
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceCtrlBench", "models");

        return new LocalTranscriptionClient(
            config, personalization: personalization, modelSource: () => model, modelsRoot: modelsRoot, conditioning: Conditioning,
            smartCleanup: CreateSmartCleanup(), foregroundProcessName: () => null,
            builtInVocabulary: Environment.GetEnvironmentVariable("VOICECTRL_BENCH_BUILTIN") == "1" ? () => BuiltInVocabulary.Shared : null);
    }

    /// <summary>
    /// Smart cleanup for the offline pipelines, when VOICECTRL_BENCH_SMART_MODELS names a models folder
    /// holding the pack (llm-qwen3-4b or llm-qwen3-1.7b, as the app lays it out). VOICECTRL_BENCH_SMART_TIER
    /// = standard | better (default better). The clips then go through the same ASR, rules, Dictionary and
    /// language model the app runs, so latency and text are the app's.
    /// </summary>
    private static SmartCleanupService? CreateSmartCleanup()
    {
        string? root = Environment.GetEnvironmentVariable("VOICECTRL_BENCH_SMART_MODELS");
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        // VOICECTRL_BENCH_MEND = off compares the pipeline without the mis-heard-word check (ticket 43).
        SmartCleanupService.MendWords = Environment.GetEnvironmentVariable("VOICECTRL_BENCH_MEND") != "off";

        SmartCleanupTier tier = Environment.GetEnvironmentVariable("VOICECTRL_BENCH_SMART_TIER") == "standard"
            ? SmartCleanupTier.Standard
            : SmartCleanupTier.Better;
        SettingsStore settings = SettingsStore.InMemory(new UserSettings { Transcription = new TranscriptionSection { SmartCleanupTier = tier } });
        var packs = new SmartCleanupPackManager(settings, root, (_, _, _) => Task.CompletedTask, () => Task.CompletedTask, () => TimeSpan.Zero);
        return new SmartCleanupService(settings, packs, 8);
    }

    private static async Task<(string? Text, string? Error)> TryTranscribeAsync(ITranscriptionClient client, byte[] wav)
    {
        try
        {
            return (await client.TranscribeAsync(wav), null);
        }
        catch (Exception ex)
        {
            return (null, ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>The Dictionary an owner who cared about every name and term in the scripts would have
    /// written, so the dictionary runs show what the app can do at best, not what a bare install does.</summary>
    private static PersonalizationStore BuildBenchDictionary(ScriptSet set)
    {
        string folder = Path.Combine(Path.GetTempPath(), "VoiceCtrlBench");
        Directory.CreateDirectory(folder);
        string dictionary = Path.Combine(folder, "dictionary.txt");
        File.WriteAllText(dictionary, CustomDictionary.Format(string.Empty, set.AllTerms().Select(t => new DictionaryEntry(t, [])).ToList()));
        return new PersonalizationStore(dictionary, Path.Combine(folder, "snippets.txt"), Path.Combine(folder, "profiles.json"));
    }

    private static string ReadGeminiKey()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        return !string.IsNullOrWhiteSpace(fromEnvironment) ? fromEnvironment : SecretsStore.Load(UserDataPaths.Secrets).GeminiApiKey;
    }

    private static double AudioSeconds(byte[] wav)
    {
        using var reader = new WaveFileReader(new MemoryStream(wav));
        return reader.TotalTime.TotalSeconds;
    }
}
