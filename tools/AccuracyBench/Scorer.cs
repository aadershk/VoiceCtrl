using System.Text;

namespace VoiceCtrl.Tools.AccuracyBench;

/// <summary>Turns saved runs into the baseline tables: WER overall and by kind of speech, how many
/// of the names and terms came out right, and how long each clip took.</summary>
public static class Scorer
{
    public static string Report(ScriptSet set, IReadOnlyList<RunResult> runs)
    {
        var byId = set.Scripts.ToDictionary(s => s.Id);
        var report = new StringBuilder();

        report.AppendLine("| Pipeline | Clips | WER | Sub / Del / Ins | Term recall | Term recall, exact case | Empty | Errors | Median s | p95 s | Real-time factor |");
        report.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (RunResult run in runs)
        {
            Summary s = Summarise(run, byId);
            report.AppendLine($"| {run.Name} | {s.Clips} | {s.Errors.Rate:P1} | {s.Errors.Substitutions} / {s.Errors.Deletions} / {s.Errors.Insertions} of {s.Errors.ReferenceWords} " +
                $"| {Percent(s.TermsFound, s.TermsSpoken)} | {Percent(s.TermsFoundExact, s.TermsSpoken)} | {s.Empty} | {s.Failed} " +
                $"| {s.MedianSeconds:0.00} | {s.P95Seconds:0.00} | {s.RealTimeFactor:0.00} |");
        }

        string[] categories = set.Scripts.Select(c => c.Category).Distinct().ToArray();
        report.AppendLine();
        report.AppendLine("WER by kind of speech:");
        report.AppendLine();
        report.AppendLine("| Pipeline | " + string.Join(" | ", categories) + " |");
        report.AppendLine("|---|" + string.Concat(categories.Select(_ => "---|")));
        foreach (RunResult run in runs)
        {
            var cells = categories.Select(category =>
            {
                WordErrors errors = default;
                foreach (ClipResult clip in run.Clips.Where(c => byId[c.Id].Category == category))
                {
                    errors += TextScoring.Compare(byId[clip.Id].Text, clip.Hypothesis ?? string.Empty);
                }

                return errors.ReferenceWords == 0 ? "-" : errors.Rate.ToString("P1");
            });
            report.AppendLine($"| {run.Name} | {string.Join(" | ", cells)} |");
        }

        foreach (RunResult run in runs)
        {
            AppendMissedTerms(report, run, byId);
        }

        return report.ToString();
    }

    private static void AppendMissedTerms(StringBuilder report, RunResult run, Dictionary<string, TestScript> byId)
    {
        var misses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (ClipResult clip in run.Clips)
        {
            foreach (string term in byId[clip.Id].Terms.Where(t => !TextScoring.TermFound(t, clip.Hypothesis ?? string.Empty, keepCase: false)))
            {
                misses[term] = misses.GetValueOrDefault(term) + 1;
            }
        }

        report.AppendLine();
        report.AppendLine($"Terms {run.Name} missed (case ignored), most often first:");
        report.AppendLine();
        report.AppendLine(misses.Count == 0
            ? "none"
            : string.Join(", ", misses.OrderByDescending(m => m.Value).ThenBy(m => m.Key).Take(40).Select(m => $"{m.Key} ({m.Value})")));
    }

    private static Summary Summarise(RunResult run, Dictionary<string, TestScript> byId)
    {
        WordErrors errors = default;
        int spoken = 0, found = 0, foundExact = 0, empty = 0, failed = 0;
        foreach (ClipResult clip in run.Clips)
        {
            TestScript script = byId[clip.Id];
            string hypothesis = clip.Hypothesis ?? string.Empty;
            errors += TextScoring.Compare(script.Text, hypothesis);
            empty += clip.Error is null && string.IsNullOrWhiteSpace(hypothesis) ? 1 : 0;
            failed += clip.Error is null ? 0 : 1;

            foreach (string term in script.Terms)
            {
                spoken++;
                found += TextScoring.TermFound(term, hypothesis, keepCase: false) ? 1 : 0;
                foundExact += TextScoring.TermFound(term, hypothesis, keepCase: true) ? 1 : 0;
            }
        }

        double[] seconds = run.Clips.Select(c => c.LatencyMs / 1000).Order().ToArray();
        double audio = run.Clips.Sum(c => c.AudioSeconds);
        return new Summary(run.Clips.Count, errors, spoken, found, foundExact, empty, failed,
            seconds.Length == 0 ? 0 : seconds[seconds.Length / 2],
            seconds.Length == 0 ? 0 : seconds[Math.Min(seconds.Length - 1, (int)Math.Ceiling(seconds.Length * 0.95) - 1)],
            audio == 0 ? 0 : run.Clips.Sum(c => c.LatencyMs / 1000) / audio);
    }

    private static string Percent(int part, int whole) => whole == 0 ? "-" : $"{(double)part / whole:P1} ({part}/{whole})";

    private sealed record Summary(
        int Clips, WordErrors Errors, int TermsSpoken, int TermsFound, int TermsFoundExact,
        int Empty, int Failed, double MedianSeconds, double P95Seconds, double RealTimeFactor);
}
