using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;

// SmartCleanupBench: runs hand-written transcripts (and the recogniser's output for the 40 spoken
// scripts) through Smart cleanup against a real llama-server. Ticket 37, research 33 L1-L4.
//
//   SmartCleanupBench --server <llama-server.exe> --model <model.gguf> --cases <cases.json>
//                     [--runs <accuracy runs json>] [--threads 8] [--out <report.md>]
//   SmartCleanupBench --server <llama-server.exe> --model <model.gguf> --rewrite-cases <json>
//                     [--threads 8] [--out <report.md>]     (rewrite by voice, ticket 39)
//   SmartCleanupBench --perapp <cases.json> --models <packs root> | --online <wav folder>
//                     [--out <report.md>]                   (per-app formatting, ticket 55)
//   SmartCleanupBench --email <cases.json> [--models <packs root> [--speech <models root> --audio <wav folder>]
//                     | --online <wav folder>] [--out <report.md>]   (Email request, ticket 57)

Dictionary<string, string> options = ParseOptions(args);
if (options.ContainsKey("email"))
{
    return await VoiceCtrl.Tools.SmartCleanupBench.EmailBench.RunAsync(options);
}

if (options.TryGetValue("perapp", out string? perAppCases))
{
    return await VoiceCtrl.Tools.SmartCleanupBench.PerAppBench.RunAsync(
        perAppCases, options.GetValueOrDefault("models"), options.GetValueOrDefault("online"), options.GetValueOrDefault("out"),
        options.ContainsKey("online") ? VoiceCtrl.Tools.SmartCleanupBench.OnlineBench.From(options) : null);
}

// Online on any provider (ticket 60): --provider gemini|nvidia|openai, --model, --text-model, --two-step true.
if (options.TryGetValue("rewrite-cases", out string? onlineRewriteCases) && options.TryGetValue("online", out string? rewriteAudio))
{
    return await VoiceCtrl.Tools.SmartCleanupBench.RewriteBench.RunOnlineAsync(
        onlineRewriteCases, rewriteAudio, VoiceCtrl.Tools.SmartCleanupBench.OnlineBench.From(options), options.GetValueOrDefault("out"));
}

if (options.TryGetValue("rewrite-cases", out string? rewriteCases) && options.TryGetValue("server", out string? rewriteServer) && options.TryGetValue("model", out string? rewriteModel))
{
    return await VoiceCtrl.Tools.SmartCleanupBench.RewriteBench.RunAsync(
        rewriteServer, rewriteModel, rewriteCases, options.TryGetValue("threads", out string? rewriteThreads) ? int.Parse(rewriteThreads) : 8, options.GetValueOrDefault("out"));
}

if (options.TryGetValue("mend-runs", out string? mendRuns) && options.TryGetValue("mend-scripts", out string? mendScripts) &&
    options.TryGetValue("server", out string? mendServer) && options.TryGetValue("model", out string? mendModel))
{
    return await VoiceCtrl.Tools.SmartCleanupBench.MendBench.RunAsync(
        mendServer, mendModel, mendRuns, mendScripts, options.TryGetValue("threads", out string? mendThreads) ? int.Parse(mendThreads) : 8,
        options.GetValueOrDefault("out"), options.TryGetValue("server-args", out string? mendArgs) ? mendArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries) : []);
}

if (!options.TryGetValue("server", out string? server) || !options.TryGetValue("model", out string? model) ||
    !options.TryGetValue("cases", out string? casesPath))
{
    Console.WriteLine("usage: SmartCleanupBench --server <exe> --model <gguf> --cases <json> [--runs <json>] [--threads N] [--out <md>]");
    return 2;
}

int threads = options.TryGetValue("threads", out string? t) ? int.Parse(t) : 8;
JsonSerializerOptions json = new() { PropertyNameCaseInsensitive = true };
List<Case> cases = JsonSerializer.Deserialize<List<Case>>(File.ReadAllText(casesPath), json)!;

// A Dictation reaches Smart cleanup already rule-cleaned (spoken line breaks become line breaks,
// "i" is capitalised), so app cases are cleaned the same way before they are run.
foreach (Case appCase in cases.Where(c => c.App is not null))
{
    appCase.Text = OfflineTextPostProcessor.Clean(appCase.Text);
}

if (options.TryGetValue("runs", out string? runsPath))
{
    cases.AddRange(LoadRecogniserCases(runsPath, json));
}

string[] serverArgs = options.TryGetValue("server-args", out string? extra)
    ? extra.Split(' ', StringSplitOptions.RemoveEmptyEntries)
    : [];
using LlamaServerModel llama = new(server, model, threads, serverArgs);

Stopwatch clock = Stopwatch.StartNew();
await llama.WarmUpAsync(CancellationToken.None);
double coldLoadSeconds = clock.Elapsed.TotalSeconds;

// One throwaway call so the first measured case is not paying for the first prompt evaluation.
await llama.CompleteAsync(new ModelRequest("Return the text.", [], "<transcript>\nhello there\n</transcript>", 16), CancellationToken.None);

List<Row> rows = [];
foreach (Case c in cases)
{
    rows.Add(await RunCaseAsync(llama, c));
    Console.Write('.');
}

Console.WriteLine();
string report = Report(rows, coldLoadSeconds, Path.GetFileName(model));
Console.WriteLine(report);
if (options.TryGetValue("out", out string? outPath))
{
    File.WriteAllText(outPath, report);
    File.WriteAllText(Path.ChangeExtension(outPath, ".rows.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
}

return 0;

static async Task<Row> RunCaseAsync(LlamaServerModel llama, Case c)
{
    DictionaryEntry[] dictionary = (c.Dictionary ?? []).Select(d => new DictionaryEntry(d.Word, d.Heard ?? [])).ToArray();
    // A case naming an app goes through the real tables and Profile rules, as a Dictation does.
    PromptContext context = c.App is not null
        ? GeminiPrompt.ContextFor(
            c.Email ? new AppProfile { EmailLayout = true } : null, c.App,
            new PromptDefaults(c.Level, ToneAwareness: true, [], []))
        : new(c.Level, c.Tone, c.Formatting switch { "structured" => "bullet or numbered steps", "prose" => "continuous prose", _ => null }, null, c.Email);

    bool gate = SmartCleanupGate.ShouldRun(c.Text, context);
    if (!gate)
    {
        return new Row(c.Id, c.Kind, c.Level, c.Text, c.Expected, Gate: false, Raw: null, Final: c.Text, Accepted: false, Reason: "gate", Ms: 0, Matches: Same(c.Text, c.Expected), Invented: 0);
    }

    ModelRequest request = SmartCleanupPrompt.Request(context, "", dictionary, c.Text, (int)(c.Text.Length / 2.5 * 1.5) + 16);

    Stopwatch timer = Stopwatch.StartNew();
    string? raw = await llama.CompleteAsync(request, CancellationToken.None);
    long ms = timer.ElapsedMilliseconds;

    string answer = Regex.Replace(Regex.Replace(raw ?? string.Empty, @"<think>.*?</think>", "", RegexOptions.Singleline), @"</?transcript>", "").Trim();
    answer = ListLeadIn.Restore(c.Text, answer);
    GuardResult verdict = SmartCleanupGuard.Check(c.Text, answer, c.Level, dictionary);
    string final = verdict.Accepted ? answer : c.Text;
    if (context.Chat != ChatLayout.None)
    {
        final = CasualChatPunctuation.Apply(final);
    }

    return new Row(c.Id, c.Kind, c.Level, c.Text, c.Expected, Gate: true, Raw: answer, Final: final, verdict.Accepted, verdict.Reason, ms,
        Matches: Same(final, c.Expected), Invented: InventedWords(c.Text, answer, dictionary));
}

// Content words the model produced that the speaker never said, judged without the guard: this is
// the model's own invention rate, which the guard exists to catch.
static int InventedWords(string input, string output, DictionaryEntry[] dictionary)
{
    HashSet<string> said = SmartCleanupGuard.Words(input).ToHashSet();
    HashSet<string> allowed = ["a", "an", "the", "to", "of", "is", "are"];
    foreach (DictionaryEntry entry in dictionary)
    {
        allowed.UnionWith(SmartCleanupGuard.Words(entry.Word));
        allowed.Add(string.Concat(SmartCleanupGuard.Words(entry.Word)));
    }

    return SmartCleanupGuard.Words(output).Count(w => !said.Contains(w) && !allowed.Contains(w) && !w.Any(char.IsDigit));
}

static bool Same(string a, string? b) =>
    b is not null && Normal(a) == Normal(b);

static string Normal(string s) => string.Join(' ', SmartCleanupGuard.Words(s));

static IEnumerable<Case> LoadRecogniserCases(string path, JsonSerializerOptions json)
{
    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
    foreach (JsonElement clip in doc.RootElement.GetProperty("Clips").EnumerateArray())
    {
        string text = OfflineTextPostProcessor.Clean(clip.GetProperty("Hypothesis").GetString() ?? string.Empty);
        yield return new Case { Id = "s" + clip.GetProperty("Id").GetString(), Kind = "recogniser", Level = "standard", Text = text, Expected = text };
    }
}

static string Report(List<Row> rows, double coldLoadSeconds, string model)
{
    StringBuilder sb = new();
    sb.AppendLine($"# Smart cleanup bench: {model}");
    sb.AppendLine();
    sb.AppendLine($"Cold load {coldLoadSeconds:0.0} s. {rows.Count} cases, {rows.Count(r => r.Gate)} past the gate.");
    sb.AppendLine();

    List<Row> ran = rows.Where(r => r.Gate).ToList();
    double[] ms = ran.Select(r => (double)r.Ms).OrderBy(x => x).ToArray();
    sb.AppendLine($"Model latency (cases that ran): p50 {Pct(ms, 0.5):0} ms, p95 {Pct(ms, 0.95):0} ms, max {(ms.Length > 0 ? ms[^1] : 0):0} ms.");
    sb.AppendLine($"Guard accepted {ran.Count(r => r.Accepted)} of {ran.Count}; rejected {ran.Count(r => !r.Accepted)}.");
    sb.AppendLine($"Raw model invented words (before the guard): {ran.Sum(r => r.Invented)} across {ran.Count(r => r.Invented > 0)} cases.");
    sb.AppendLine($"Final text matches the target: {rows.Count(r => r.Matches)} of {rows.Count}.");
    sb.AppendLine();
    sb.AppendLine("| kind | cases | ran | accepted | matches | invented (raw) | p50 ms |");
    sb.AppendLine("|---|---|---|---|---|---|---|");
    foreach (IGrouping<string, Row> g in rows.GroupBy(r => r.Kind))
    {
        double[] gm = g.Where(r => r.Gate).Select(r => (double)r.Ms).OrderBy(x => x).ToArray();
        sb.AppendLine($"| {g.Key} | {g.Count()} | {g.Count(r => r.Gate)} | {g.Count(r => r.Accepted)} | {g.Count(r => r.Matches)} | {g.Sum(r => r.Invented)} | {Pct(gm, 0.5):0} |");
    }

    sb.AppendLine();
    sb.AppendLine("## Cases that do not match the target");
    foreach (Row r in rows.Where(r => !r.Matches && r.Kind != "recogniser"))
    {
        sb.AppendLine();
        sb.AppendLine($"**{r.Id}** ({r.Kind}, {r.Level}, gate {(r.Gate ? "ran" : "skipped")}, {(r.Accepted ? "accepted" : "kept rules: " + r.Reason)}, {r.Ms} ms)");
        sb.AppendLine($"- in:  {r.Input.Replace("\n", " / ")}");
        sb.AppendLine($"- out: {r.Raw?.Replace("\n", " / ") ?? "(not called)"}");
        sb.AppendLine($"- want: {r.Expected?.Replace("\n", " / ")}");
    }

    List<Row> fluent = rows.Where(r => r.Kind == "recogniser" && r.Gate).ToList();
    sb.AppendLine();
    sb.AppendLine($"## Recogniser transcripts past the gate: {fluent.Count}");
    foreach (Row r in fluent)
    {
        sb.AppendLine($"- {r.Id} ({r.Ms} ms, {(r.Accepted ? "accepted" : r.Reason)}): {r.Raw?.Replace("\n", " / ")}");
    }

    return sb.ToString();
}

static double Pct(double[] sorted, double p) => sorted.Length == 0 ? 0 : sorted[(int)Math.Min(sorted.Length - 1, Math.Floor(p * sorted.Length))];

static Dictionary<string, string> ParseOptions(string[] args)
{
    Dictionary<string, string> result = [];
    for (int i = 0; i + 1 < args.Length; i++)
    {
        if (args[i].StartsWith("--", StringComparison.Ordinal))
        {
            result[args[i][2..]] = args[i + 1];
        }
    }

    return result;
}

internal sealed class Case
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Level { get; set; } = "standard";
    public string? Formatting { get; set; }
    public string? Tone { get; set; }
    public string? App { get; set; }
    public bool Email { get; set; }
    public string Text { get; set; } = "";
    public string? Expected { get; set; }
    public List<DictCase>? Dictionary { get; set; }
}

internal sealed class DictCase
{
    public string Word { get; set; } = "";
    public List<string>? Heard { get; set; }
}

internal sealed record Row(
    string Id, string Kind, string Level, string Input, string? Expected, bool Gate, string? Raw, string Final, bool Accepted, string? Reason, long Ms, bool Matches, int Invented);
