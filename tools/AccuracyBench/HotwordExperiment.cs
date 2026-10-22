using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using SherpaOnnx;
using VoiceCtrl.Core.Audio;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Tools.AccuracyBench;

/// <summary>
/// Ticket 36c: decode-time hotword biasing on the Parakeet models (sherpa-onnx modified_beam_search),
/// measured on the same clips and scorer as the other runs. It drives sherpa directly rather than the
/// app's client, so nothing ships from here until a variant clears the bar.
/// </summary>
public static class HotwordExperiment
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <param name="pipeline">parakeet-v2 or parakeet-v3.</param>
    /// <param name="score">hotwords_score; 0 means plain greedy_search with the same plumbing (control).</param>
    /// <param name="mode">raw = words plus bpe_vocab; tokens = pre-tokenised lines from tokens.txt.</param>
    /// <param name="corrector">Apply the Dictionary corrector after decoding, as the app does.</param>
    /// <param name="repeats">How many times each clip is decoded (beam search is judged on repeats).</param>
    public static int Run(ScriptSet set, string pipeline, double score, string mode, bool corrector, int repeats)
    {
        OfflineModel model = pipeline == "parakeet-v3" ? OfflineModels.European : OfflineModels.English;
        string installed = model.PathUnder(UserDataPaths.Models);
        string modelDirectory = Directory.Exists(installed)
            ? installed
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceCtrlBench", "models", model.Directory);

        // VOICECTRL_BENCH_HOTWORDS_BUILTIN=N biases toward the N most useful built-in terms instead of
        // the scripts' own (ticket 36b): the list as a user with an empty Dictionary would have it.
        int builtInCount = int.TryParse(Environment.GetEnvironmentVariable("VOICECTRL_BENCH_HOTWORDS_BUILTIN"), out int n) ? n : 0;
        IReadOnlyList<string> terms = builtInCount > 0
            ? BuiltInVocabulary.Shared.Terms.Where(t => t.Split(' ').Length <= 2).Take(builtInCount).ToList()
            : set.AllTerms();
        string work = Path.Combine(Path.GetTempPath(), "VoiceCtrlBench", "hotwords");
        Directory.CreateDirectory(work);

        string hotwordsFile = Path.Combine(work, $"{pipeline}-{mode}.txt");
        File.WriteAllLines(hotwordsFile, HotwordLines(terms, modelDirectory, mode));
        string bpeVocab = Path.Combine(work, pipeline + ".bpe.vocab");
        File.WriteAllLines(bpeVocab, File.ReadLines(Path.Combine(modelDirectory, "tokens.txt"))
            .Select(l => l.Split(' ')).Select(p => $"{p[0]}\t{-int.Parse(p[1])}"));

        var config = new OfflineRecognizerConfig();
        config.ModelConfig.Tokens = Path.Combine(modelDirectory, "tokens.txt");
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.NumThreads = 4;
        config.ModelConfig.Transducer.Encoder = Path.Combine(modelDirectory, "encoder.int8.onnx");
        config.ModelConfig.Transducer.Decoder = Path.Combine(modelDirectory, "decoder.int8.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(modelDirectory, "joiner.int8.onnx");
        config.ModelConfig.ModelType = "nemo_transducer";
        if (score > 0)
        {
            config.DecodingMethod = "modified_beam_search";
            config.MaxActivePaths = 4;
            config.HotwordsFile = hotwordsFile;
            config.HotwordsScore = (float)score;
            config.ModelConfig.ModelingUnit = "bpe";
            config.ModelConfig.BpeVocab = bpeVocab;
        }
        else
        {
            config.DecodingMethod = "greedy_search";
        }

        string label = $"{pipeline}-hw-{mode}-{score:0.0}{(corrector ? "-corr" : "")}{(builtInCount > 0 ? "-bi" + builtInCount : "")}";
        Console.WriteLine(label + ": loading...");
        using var recognizer = new OfflineRecognizer(config);

        PersonalizationStore personalization = Dictionary(work, terms);
        var clips = new List<ClipResult>();
        int empty = 0, yeah = 0, decodes = 0;
        foreach (TestScript script in set.Scripts.Where(s => File.Exists(set.AudioPath(s))))
        {
            float[] samples = SpeechConditioner.Condition(ReadSamples(set.AudioPath(script)), SpeechConditioning.TrimSilence);
            var variants = new HashSet<string>();
            for (int i = 0; i < repeats; i++)
            {
                var timer = Stopwatch.StartNew();
                using OfflineStream stream = recognizer.CreateStream();
                stream.AcceptWaveform(16000, samples);
                recognizer.Decode(stream);
                string raw = stream.Result.Text.Trim();
                timer.Stop();
                decodes++;

                if (raw.Length == 0) { empty++; }
                else if (raw.TrimEnd('.', '!', ' ').Equals("Yeah", StringComparison.OrdinalIgnoreCase)) { yeah++; }
                variants.Add(raw);

                if (i == 0)
                {
                    string text = OfflineTextPostProcessor.Clean(raw, false);
                    if (corrector) { text = DictionaryCorrector.Apply(text, personalization.Dictionary); }
                    clips.Add(new ClipResult(script.Id, raw.Length == 0 ? null : text, timer.Elapsed.TotalMilliseconds, samples.Length / 16000.0, null));
                }
            }

            if (variants.Count > 1)
            {
                Console.WriteLine($"  {script.Id}: {variants.Count} different outputs over {repeats} decodes");
            }
        }

        Console.WriteLine($"{label}: {decodes} decodes, {empty} empty, {yeah} \"Yeah.\"");
        string dir = Path.Combine(set.Root, "experiments");
        Directory.CreateDirectory(dir);
        var result = new RunResult(label, corrector, DateTime.UtcNow, clips);
        File.WriteAllText(Path.Combine(dir, label + ".json"), JsonSerializer.Serialize(result, Json));
        Console.WriteLine(Scorer.Report(set, [result]));
        return 0;
    }

    /// <summary>One or more lines per term. raw: the word itself. tokens: the term cut into the model's
    /// own pieces by longest match (the true merge order is not available, so a few cuts per term).</summary>
    private static IEnumerable<string> HotwordLines(IReadOnlyList<string> terms, string modelDirectory, string mode)
    {
        if (mode == "raw")
        {
            return terms;
        }

        HashSet<string> vocab = File.ReadLines(Path.Combine(modelDirectory, "tokens.txt")).Select(l => l.Split(' ')[0]).ToHashSet();
        var lines = new List<string>();
        foreach (string term in terms)
        {
            foreach (string word in new[] { term, term.ToLowerInvariant() }.Distinct())
            {
                foreach (string cut in Cuts(word.Replace(" ", "▁"), vocab))
                {
                    lines.Add(cut);
                }
            }
        }

        return lines.Distinct();
    }

    /// <summary>Greedy longest-match from the left, and from the right: two plausible BPE cuts.</summary>
    private static IEnumerable<string> Cuts(string text, HashSet<string> vocab)
    {
        string marked = "▁" + text.TrimStart('▁');
        foreach (bool fromRight in new[] { false, true })
        {
            var pieces = new List<string>();
            string rest = marked;
            bool ok = true;
            while (rest.Length > 0 && ok)
            {
                ok = false;
                for (int len = rest.Length; len >= 1; len--)
                {
                    string piece = fromRight ? rest[^len..] : rest[..len];
                    if (vocab.Contains(piece))
                    {
                        pieces.Add(piece);
                        rest = fromRight ? rest[..^len] : rest[len..];
                        ok = true;
                        break;
                    }
                }
            }

            if (ok)
            {
                if (fromRight) { pieces.Reverse(); }
                yield return string.Join(' ', pieces);
            }
        }
    }

    private static PersonalizationStore Dictionary(string folder, IReadOnlyList<string> terms)
    {
        string dictionary = Path.Combine(folder, "dictionary.txt");
        File.WriteAllText(dictionary, CustomDictionary.Format(string.Empty, terms.Select(t => new DictionaryEntry(t, [])).ToList()));
        return new PersonalizationStore(dictionary, Path.Combine(folder, "snippets.txt"), Path.Combine(folder, "profiles.json"));
    }

    private static float[] ReadSamples(string path)
    {
        using var reader = new AudioFileReader(path);
        var samples = new List<float>();
        var buffer = new float[16000];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        return [.. samples];
    }
}
