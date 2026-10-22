using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Rewrite;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Tools.SmartCleanupBench;

/// <summary>
/// Rewrite by voice (ticket 39) against a real llama-server: each case is an instruction and a text,
/// and the reply is judged by checks rather than an exact target, since a rewrite has many right
/// answers. The prompt, examples and clean-up are the app's own (<see cref="RewriteText"/>).
/// <code>SmartCleanupBench --server X --model Y --rewrite-cases rewrite-cases.json [--threads 8] [--out report.md]</code>
/// </summary>
internal static partial class RewriteBench
{
    public static async Task<int> RunAsync(string server, string model, string casesPath, int threads, string? outPath)
    {
        JsonSerializerOptions json = new() { PropertyNameCaseInsensitive = true };
        List<RewriteCase> cases = JsonSerializer.Deserialize<List<RewriteCase>>(File.ReadAllText(casesPath), json)!;

        using LlamaServerModel llama = new(server, model, threads);
        Stopwatch clock = Stopwatch.StartNew();
        await llama.WarmUpAsync(CancellationToken.None);
        double coldLoad = clock.Elapsed.TotalSeconds;

        // The prompt the app primes on the key press, so the first case is not a cold prompt.
        await llama.CompleteAsync(new ModelRequest(RewriteText.System([]), RewriteText.ExamplePairs, RewriteText.User("fix it", "Hello."), 1), CancellationToken.None);

        List<RewriteRow> rows = [];
        foreach (RewriteCase c in cases)
        {
            rows.Add(await RunCaseAsync(llama, c));
            Console.Write('.');
        }

        Console.WriteLine();
        string report = Report(rows, coldLoad, Path.GetFileName(model));
        Console.WriteLine(report);
        if (outPath is not null)
        {
            File.WriteAllText(outPath, report);
            File.WriteAllText(Path.ChangeExtension(outPath, ".rows.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }

        return 0;
    }

    private static async Task<RewriteRow> RunCaseAsync(LlamaServerModel llama, RewriteCase c)
    {
        DictionaryEntry[] dictionary = (c.Dictionary ?? []).Select(word => new DictionaryEntry(word, [])).ToArray();
        int words = c.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        ModelRequest request = new(
            RewriteText.System(dictionary), RewriteText.ExamplePairs, RewriteText.User(c.Instruction, c.Text),
            MaxTokens: (int)(c.Text.Length / 2.5 * 2) + 160);

        using CancellationTokenSource timeout = new(SmartCleanupService.RewriteTimeoutFor(SmartCleanupTier.Better, words));
        Stopwatch timer = Stopwatch.StartNew();
        string? raw;
        try
        {
            raw = await llama.CompleteAsync(request, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return new RewriteRow(c.Id, c.Kind, c.Instruction, c.Text, null, Passed: false, ["timed out"], timer.ElapsedMilliseconds);
        }

        long ms = timer.ElapsedMilliseconds;
        string? output = RewriteText.Clean(raw, c.Text);
        if (output is null)
        {
            return new RewriteRow(c.Id, c.Kind, c.Instruction, c.Text, raw, Passed: false, ["no usable reply"], ms);
        }

        List<string> failures = Check(c, output);
        return new RewriteRow(c.Id, c.Kind, c.Instruction, c.Text, output, failures.Count == 0, failures, ms);
    }

    private static List<string> Check(RewriteCase c, string output)
    {
        List<string> failures = [];
        string lower = output.ToLowerInvariant();
        int inWords = c.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        int outWords = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        int lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

        foreach (string must in c.All ?? [])
        {
            if (!lower.Contains(must.ToLowerInvariant(), StringComparison.Ordinal))
            {
                failures.Add($"missing \"{must}\"");
            }
        }

        if (c.Any is { Count: > 0 } any && !any.Any(a => lower.Contains(a.ToLowerInvariant(), StringComparison.Ordinal)))
        {
            failures.Add($"none of [{string.Join(", ", any)}]");
        }

        foreach (string banned in c.None ?? [])
        {
            if (lower.Contains(banned.ToLowerInvariant(), StringComparison.Ordinal))
            {
                failures.Add($"contains \"{banned}\"");
            }
        }

        if (c.MaxRatio is { } ratio && outWords > inWords * ratio)
        {
            failures.Add($"{outWords} words is over {ratio:0.##} x {inWords}");
        }

        if (c.MinLines is { } minLines && lines < minLines)
        {
            failures.Add($"{lines} lines, wanted {minLines}+");
        }

        if (c.MaxLines is { } maxLines && lines > maxLines)
        {
            failures.Add($"{lines} lines, wanted at most {maxLines}");
        }

        failures.AddRange(CheckGrouping(c, output));
        return failures;
    }

    /// <summary>Ticket 58: a list whose items share groups names each group once, with its items
    /// indented under it; a list with no groups stays flat, with no heading the speaker never gave.</summary>
    private static IEnumerable<string> CheckGrouping(RewriteCase c, string output)
    {
        string[] lines = output.Split('\n').Select(line => line.TrimEnd()).Where(line => line.Length > 0).ToArray();
        bool nested = lines.Any(line => NestedItem().IsMatch(line));

        if (c.Groups is { Count: > 0 } groups)
        {
            if (!nested)
            {
                yield return "not nested";
            }

            foreach (string group in groups)
            {
                int naming = lines.Count(line => !NestedItem().IsMatch(line) &&
                    Regex.IsMatch(line, $@"\b{Regex.Escape(group)}\b", RegexOptions.IgnoreCase));
                if (naming != 1)
                {
                    yield return $"group \"{group}\" heads {naming} lines, wanted 1";
                }
            }
        }

        if (c.Flat == true)
        {
            // A heading must be a group the text names ("On Monday:"); nesting is right only under such
            // a heading, and then only when there is one: a list of one group may be nested under it.
            HashSet<string> said = Words(c.Text).ToHashSet();
            string[] headings = lines.Where(line => HeadingOnly().IsMatch(line)).ToArray();
            if (headings.Any(line => Words(line).Any(word => !said.Contains(word))))
            {
                yield return "added a heading";
            }
            else if (nested && headings.Length != 1)
            {
                yield return "nested a list with no groups";
            }
        }
    }

    private static IEnumerable<string> Words(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}']+").Select(match => match.Value);

    [GeneratedRegex(@"^(\s{2,}|\t)([-*•]|\d+[.)])\s")]
    private static partial Regex NestedItem();

    [GeneratedRegex(@"^\s*([-*•]\s*)?\**[^.!?]{1,40}:\**$")]
    private static partial Regex HeadingOnly();

    private static string Report(List<RewriteRow> rows, double coldLoad, string model)
    {
        StringBuilder sb = new();
        double[] ms = rows.Select(r => (double)r.Ms).OrderBy(x => x).ToArray();
        sb.AppendLine($"# Rewrite bench: {model}");
        sb.AppendLine();
        sb.AppendLine($"Cold load {coldLoad:0.0} s. {rows.Count} cases, {rows.Count(r => r.Passed)} passed ({100.0 * rows.Count(r => r.Passed) / rows.Count:0}%).");
        sb.AppendLine($"Latency: p50 {ms[ms.Length / 2]:0} ms, p95 {ms[(int)(ms.Length * 0.95)]:0} ms, max {ms[^1]:0} ms.");
        sb.AppendLine();
        sb.AppendLine("| kind | cases | passed | p50 ms |");
        sb.AppendLine("|---|---|---|---|");
        foreach (IGrouping<string, RewriteRow> g in rows.GroupBy(r => r.Kind))
        {
            double[] gm = g.Select(r => (double)r.Ms).OrderBy(x => x).ToArray();
            sb.AppendLine($"| {g.Key} | {g.Count()} | {g.Count(r => r.Passed)} | {gm[gm.Length / 2]:0} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Every case");
        foreach (RewriteRow r in rows)
        {
            sb.AppendLine();
            sb.AppendLine($"**{r.Id}** ({r.Kind}, {r.Ms} ms): {(r.Passed ? "pass" : "FAIL: " + string.Join("; ", r.Failures))}");
            sb.AppendLine($"- say: {r.Instruction}");
            sb.AppendLine($"- in:  {r.Input.Replace("\n", " / ")}");
            sb.AppendLine($"- out: {r.Output?.Replace("\n", " / ") ?? "(none)"}");
        }

        return sb.ToString();
    }
}

internal sealed class RewriteCase
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Instruction { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string>? All { get; set; }
    public List<string>? Any { get; set; }
    public List<string>? None { get; set; }
    public List<string>? Dictionary { get; set; }
    public double? MaxRatio { get; set; }
    public int? MinLines { get; set; }
    public int? MaxLines { get; set; }

    /// <summary>The groups a list should be nested under, each named once (ticket 58).</summary>
    public List<string>? Groups { get; set; }

    /// <summary>True for a list with no groups, which must stay flat (ticket 58).</summary>
    public bool? Flat { get; set; }
}

internal sealed record RewriteRow(string Id, string Kind, string Instruction, string Input, string? Output, bool Passed, List<string> Failures, long Ms);
