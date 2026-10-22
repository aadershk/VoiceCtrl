using System.Diagnostics;
using System.Text;
using System.Text.Json;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Tools.SmartCleanupBench;

/// <summary>
/// Mending mis-heard words (ticket 43) against a real llama-server. Each case is a recogniser
/// transcript with the words the speaker actually said, so every pair the model proposes is judged:
/// a good pair turns a word that is not in the script into one that is, a bad pair changes a word
/// the recogniser had right. The prompt, parsing and checks are the app's own (<see cref="SmartCleanupMender"/>).
/// <code>SmartCleanupBench --server X --model Y --mend-runs run.json[,run2.json] --mend-scripts scripts.json[,scripts2.json] [--threads 8] [--out report.md]</code>
/// </summary>
internal static class MendBench
{
    public static async Task<int> RunAsync(
        string server, string model, string runPaths, string scriptPaths, int threads, string? outPath, IReadOnlyList<string> serverArgs)
    {
        List<MendCase> cases = [];
        string[] runs = runPaths.Split(',');
        string[] scripts = scriptPaths.Split(',');
        for (int set = 0; set < runs.Length; set++)
        {
            Dictionary<string, string> reference = JsonDocument.Parse(File.ReadAllText(scripts[set])).RootElement.EnumerateArray()
                .ToDictionary(s => s.GetProperty("id").GetString()!, s => s.GetProperty("text").GetString()!);
            foreach (JsonElement clip in JsonDocument.Parse(File.ReadAllText(runs[set])).RootElement.GetProperty("Clips").EnumerateArray())
            {
                string id = clip.GetProperty("Id").GetString()!;
                cases.Add(new MendCase($"{set}-{id}", clip.GetProperty("Hypothesis").GetString() ?? string.Empty, reference[id]));
            }
        }

        BuiltInVocabulary vocabulary = BuiltInVocabulary.Shared;
        using LlamaServerModel llama = new(server, model, threads, serverArgs);
        await llama.WarmUpAsync(CancellationToken.None);
        await llama.CompleteAsync(SmartCleanupMender.Request("hello", []), CancellationToken.None);

        StringBuilder log = new();
        List<double> latencies = [];
        int good = 0, bad = 0, refused = 0, applied = 0, withPairs = 0, skipped = 0;
        foreach (MendCase c in cases)
        {
            IReadOnlyList<string> unknown = SmartCleanupMender.UnknownWords(c.Text, vocabulary, []);
            if (unknown.Count == 0)
            {
                skipped++;
                Console.Write('_');
                continue;
            }

            Stopwatch timer = Stopwatch.StartNew();
            string? reply = await llama.CompleteAsync(SmartCleanupMender.Request(c.Text, unknown), CancellationToken.None);
            double seconds = timer.Elapsed.TotalSeconds;
            latencies.Add(seconds);
            IReadOnlyList<WordMend> pairs = SmartCleanupMender.Parse(reply);
            string mended = SmartCleanupMender.Apply(c.Text, pairs, unknown, vocabulary, []);
            if (pairs.Count > 0)
            {
                withPairs++;
            }

            HashSet<string> said = SmartCleanupGuard.Words(c.Reference).ToHashSet();
            foreach (WordMend pair in pairs)
            {
                bool wasWrong = !said.Contains(pair.From);
                bool nowRight = said.Contains(pair.To);
                bool fromUnknown = unknown.Contains(pair.From);
                bool accepted = SmartCleanupMender.Apply(c.Text, [pair], unknown, vocabulary, []) != c.Text;
                string verdict = wasWrong && nowRight ? "GOOD" : wasWrong ? "neutral (wrong word, still wrong)" : "BAD (was right)";
                if (accepted)
                {
                    applied++;
                    good += wasWrong && nowRight ? 1 : 0;
                    bad += wasWrong ? 0 : 1;
                }
                else
                {
                    refused++;
                }

                log.AppendLine($"{c.Id} {seconds:0.0}s {(accepted ? "applied" : "refused")} {(fromUnknown ? "unknown" : "ordinary")} {pair.From} -> {pair.To}  [{verdict}]");
            }

            Console.Write('.');
        }

        double[] sorted = latencies.Order().ToArray();
        StringBuilder report = new();
        report.AppendLine($"# Mend bench: {Path.GetFileName(model)}, {cases.Count} transcripts");
        report.AppendLine();
        report.AppendLine($"{skipped} transcripts had no unknown word, so no call; {latencies.Count} calls.");
        report.AppendLine($"Latency p50 {sorted[sorted.Length / 2]:0.00} s, p95 {sorted[(int)(sorted.Length * 0.95)]:0.00} s, max {sorted[^1]:0.00} s.");
        report.AppendLine($"{withPairs} transcripts got pairs. Pairs applied {applied} (good {good}, bad {bad}), refused by the checks {refused}.");
        report.AppendLine();
        report.Append(log);
        Console.WriteLine();
        Console.WriteLine(report);
        if (outPath is not null)
        {
            File.WriteAllText(outPath, report.ToString());
        }

        return 0;
    }

    private sealed record MendCase(string Id, string Text, string Reference);
}
