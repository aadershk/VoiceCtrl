using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Tools.SmartCleanupBench;

/// <summary>
/// The per-app bench (ticket 55): each spoken message as it lands in an email, chat, document, AI or
/// other app, scored against a hand-written target for that app's kind. It runs the app's own
/// pipeline, Offline (rules, Mend, Smart cleanup, the paste layout, with the model primed at the
/// hotkey press as in the app) or Online (Gemini hears a spoken recording of the message).
/// A case may name a Style (ticket 56): its kind of app is set to that Style for the case, and the
/// Style's capitals, closing punctuation and register are scored too.
///
///   SmartCleanupBench --perapp cases.json --models &lt;packs root&gt; [--out report.md]
///   SmartCleanupBench --perapp cases.json --online &lt;folder of message-id.wav&gt; [--out report.md]
/// </summary>
internal static partial class PerAppBench
{
    private const double WordsPerSecondSpoken = 2.5;

    public static async Task<int> RunAsync(string casesPath, string? modelsRoot, string? audioFolder, string? outPath, OnlineBench? online = null)
    {
        List<PerAppCase> cases = JsonSerializer.Deserialize<List<PerAppCase>>(
            File.ReadAllText(casesPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        List<PerAppRow> rows = audioFolder is null
            ? await RunOfflineAsync(cases, modelsRoot ?? throw new ArgumentException("--models is needed Offline"))
            : await RunOnlineAsync(cases, audioFolder, online ?? throw new ArgumentException("No Online provider"));

        string report = Report(rows, audioFolder is null ? "Offline (Smart cleanup, Qwen3-4B)" : online!.Describe);
        Console.WriteLine(report);
        if (outPath is not null)
        {
            File.WriteAllText(outPath, report);
            File.WriteAllText(Path.ChangeExtension(outPath, ".rows.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }

        return 0;
    }

    private static async Task<List<PerAppRow>> RunOfflineAsync(List<PerAppCase> cases, string modelsRoot)
    {
        SettingsStore settings = SettingsStore.InMemory(new UserSettings { Transcription = new TranscriptionSection { SmartCleanupTier = SmartCleanupTier.Better } });
        var packs = new SmartCleanupPackManager(settings, modelsRoot, (_, _, _) => Task.CompletedTask, () => Task.CompletedTask, () => TimeSpan.Zero);
        using var smartCleanup = new SmartCleanupService(settings, packs, 8);
        if (!smartCleanup.IsAvailable)
        {
            throw new InvalidOperationException($"No Smart cleanup pack under {modelsRoot}.");
        }

        AppConfig config = ConfigLoader.Load(new UserSettings(), string.Empty);
        PerAppCase current = cases[0];
        string recordPath = Path.Combine(Path.GetTempPath(), $"perapp-engine-{Environment.ProcessId}.json");
        var engine = new EngineRecorder(recordPath, () => true);
        using var client = new LocalTranscriptionClient(
            config,
            smartCleanup: smartCleanup,
            cleanupLevel: () => CleanupLevels.Standard,
            foregroundProcessName: () => current.App,
            foregroundWindowTitle: () => current.Title,
            builtInVocabulary: () => BuiltInVocabulary.Shared,
            engineRecorder: engine,
            styles: () => StylesFor(current));

        // Loads the model once, as the first Dictation of a session would.
        Stopwatch load = Stopwatch.StartNew();
        await smartCleanup.PrimeAsync(Context(current), config.StyleNotes, [], CancellationToken.None);
        Console.WriteLine($"Model loaded and primed in {load.Elapsed.TotalSeconds:0.0} s.");

        // The server's own count of each answer; a prime writes a single token, so it is left out.
        List<ModelTimings> answers = [];
        LlamaServerModel.Timed += timings =>
        {
            if (timings.WrittenTokens > 1)
            {
                lock (answers)
                {
                    answers.Add(timings);
                }
            }
        };

        List<PerAppRow> rows = [];
        foreach (PerAppCase c in cases)
        {
            current = c;
            lock (answers)
            {
                answers.Clear();
            }

            // The hotkey press primes the model for the app in front while the user speaks; the wait
            // that counts starts when they stop.
            Task prime = smartCleanup.PrimeAsync(Context(c), config.StyleNotes, [], CancellationToken.None);
            double speakingSeconds = Math.Max(1.5, Words(c.Text) / WordsPerSecondSpoken);
            await Task.WhenAny(prime, Task.Delay(TimeSpan.FromSeconds(speakingSeconds)));
            bool primedInTime = prime.IsCompleted;

            Stopwatch clock = Stopwatch.StartNew();
            string output = await client.TranscribeRecordedAsync(c.Text, CancellationToken.None);
            long ms = clock.ElapsedMilliseconds;
            await prime;

            EngineEntry entry = engine.Entries[^1];
            string served;
            lock (answers)
            {
                served = string.Join("; ", answers.Select(t =>
                    $"read {t.PromptTokens} ({t.CachedTokens} cached) in {t.PromptMs:0} ms, wrote {t.WrittenTokens} in {t.WrittenMs:0} ms"));
            }

            string engineNote = $"cleanup {entry.SmartCleanup}{(entry.GuardRejections.Count > 0 ? $" ({string.Join(", ", entry.GuardRejections)})" : "")}, " +
                                $"mend {entry.Mend}, model {entry.ModelMs} ms{(served.Length > 0 ? $" [{served}]" : "")}";
            rows.Add(Score(c, output, ms, primedInTime) with { Engine = engineNote });
            Console.WriteLine($"{c.Id}: {ms} ms, {engineNote}");
        }

        File.Delete(recordPath);
        return rows;
    }

    private static async Task<List<PerAppRow>> RunOnlineAsync(List<PerAppCase> cases, string audioFolder, OnlineBench online)
    {
        PerAppCase current = cases[0];
        using ITranscriptionClient client = online.Transcriber(() => current.App, () => current.Title, () => StylesFor(current));

        // A free Gemini key allows about ten requests a minute; NVIDIA's free tier is shared and busy
        // now and then, so a short pause and a retry is enough there.
        bool gemini = online.Endpoint.Provider == VoiceCtrl.Core.Online.OnlineProvider.Gemini;

        List<PerAppRow> rows = [];
        foreach (PerAppCase c in cases)
        {
            current = c;
            byte[] wav = File.ReadAllBytes(Path.Combine(audioFolder, c.Message + ".wav"));
            string output = string.Empty;
            long ms = 0;

            // A free key allows about ten requests a minute, and the model is sometimes busy: pace the
            // calls, and try a refused one again after a pause. Only the answered call is timed.
            for (int attempt = 1; attempt <= 4; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt == 1 ? (gemini ? 7 : 1) : (gemini ? 30 : 5)));
                Stopwatch clock = Stopwatch.StartNew();
                try
                {
                    output = await client.TranscribeAsync(wav) ?? string.Empty;
                    ms = clock.ElapsedMilliseconds;
                    break;
                }
                catch (Exception ex)
                {
                    output = $"[error: {ex.GetType().Name}: {ex.Message}]";
                    Console.WriteLine($"{c.Id}: attempt {attempt} refused ({ex.Message.Split('.')[0]})");
                }
            }

            rows.Add(Score(c, output, ms, primedInTime: true));
            Console.WriteLine($"{c.Id}: {ms} ms");
        }

        Console.WriteLine();
        return rows;
    }

    private static PromptContext Context(PerAppCase c) =>
        GeminiPrompt.ContextFor(null, c.App, new PromptDefaults(CleanupLevels.Standard, ToneAwareness: true, [], [], Styles: StylesFor(c)), c.Title);

    /// <summary>A fresh install's Styles, with the case's kind of app set to the case's Style.</summary>
    private static StyleSettings StylesFor(PerAppCase c) =>
        StyleNames.Parse(c.Style) is { } style
            ? new StyleSettings().With(AppKinds.Of(c.App, c.Title), style)
            : new StyleSettings();

    // ---- Scoring

    /// <summary>
    /// Words: the target's words, give or take one in twenty (a contraction counts as its two words,
    /// punctuation and case never count). Layout, always: the target's list items, no more and no
    /// fewer. Then by kind, since how thoughts group into lines or paragraphs is a judgement the
    /// target makes one way of several:
    ///   email     the greeting and sign-off lines exactly as in the target, paragraphs within one
    ///   document  paragraphs within one
    ///   chat      no blank line, no line ending in a full stop, lines within one fewer or two more
    ///   ai, other the target's exact lines
    /// </summary>
    private static PerAppRow Score(PerAppCase c, string output, long ms, bool primedInTime)
    {
        List<string> want = Tokens(c.Expected);
        List<string> got = Tokens(output);
        int distance = EditDistance(want, got);
        bool wordsOk = distance <= 1 + want.Count / 20;

        string wantShape = Shape(c.Expected);
        string gotShape = Shape(output);
        bool layoutExact = wantShape == gotShape;
        bool sameItems = wantShape.Count(ch => ch == 'L') == gotShape.Count(ch => ch == 'L');
        bool layoutOk = sameItems && c.AppKind switch
        {
            "email" => Math.Abs(Blocks(wantShape) - Blocks(gotShape)) <= 1 &&
                       StartsLikeAnEmail(c.Expected) == StartsLikeAnEmail(output) && EndsLikeAnEmail(c.Expected) == EndsLikeAnEmail(output),
            "document" => Math.Abs(Blocks(wantShape) - Blocks(gotShape)) <= 1,
            // Ticket 59: like a typed text. A personal chat's message is the target's one paragraph
            // (and its list); a work chat's keeps the target's paragraphs, give or take one. Never a
            // line a sentence: no more prose lines than the target.
            "chat" or "workChat" or "personalChat" =>
                      (c.Style == "formal" || !output.Split('\n').Any(EndsInFullStop)) &&
                      gotShape.Count(ch => ch == 'T') <= wantShape.Count(ch => ch == 'T') + (IsWorkChat(c) ? 1 : 0) &&
                      Math.Abs(Blocks(wantShape) - Blocks(gotShape)) <= (IsWorkChat(c) ? 1 : 0),
            _ => layoutExact,
        };

        bool styleOk = StyleOk(c.Style, c.Expected, output);
        return new PerAppRow(c.Id, c.Message, c.AppKind, c.App, c.Title, c.Text, c.Expected, output, ms, primedInTime,
            wordsOk, distance, layoutOk, layoutExact, wordsOk && layoutOk && styleOk) { Style = c.Style, StyleOk = styleOk };
    }

    /// <summary>
    /// What the case's Style changes (ticket 56), against the target:
    ///   formal      no contraction and no "yeah", "gonna"; the text ends in . ? or ! where the target's does
    ///   casual      no line ends in a full stop; the first letter's case as in the target
    ///   veryCasual  no line ends in a full stop; each line starts lowercase, but for "I" and names the target capitalises
    ///   excited     no line ends in a full stop; at least one "!", and no more than one fewer than the target
    /// No Style: nothing more to check.
    /// </summary>
    private static bool StyleOk(string? style, string expected, string output)
    {
        string[] want = expected.Trim().Split('\n').Where(line => line.Trim().Length > 0).ToArray();
        string[] got = output.Trim().Split('\n').Where(line => line.Trim().Length > 0).ToArray();
        bool noClosingFullStop = !got.Any(EndsInFullStop);
        bool sameFirstCase = FirstLetterCase(want.FirstOrDefault()) == FirstLetterCase(got.FirstOrDefault());
        return style switch
        {
            "formal" => !InformalWord().IsMatch(output) &&
                        (EndsASentence(want.LastOrDefault()) == EndsASentence(got.LastOrDefault())),
            "casual" => noClosingFullStop && sameFirstCase,
            // Chat lines can group thoughts differently from the target, so each line is judged alone.
            "veryCasual" => noClosingFullStop && got.All(line => StartsVeryCasually(line, expected)),
            "excited" => noClosingFullStop && output.Count(ch => ch == '!') is var bangs &&
                         bangs > 0 && bangs >= expected.Count(ch => ch == '!') - 1,
            _ => true,
        };
    }

    /// <summary>A Very casual line: its first word lowercase, unless it is "I" ("I'm") or a name the
    /// target writes with a capital too.</summary>
    private static bool StartsVeryCasually(string line, string expected)
    {
        string first = ListItem().Replace(line, string.Empty).TrimStart().Split(' ', 2)[0];
        if (first.Length == 0 || !char.IsUpper(first[0]))
        {
            return true;
        }

        string word = first.Split('\'', '’')[0].TrimEnd(',', ':', '!', '?');
        // A Very casual target capitalises only "I" and names, so a capital it has too is a name's.
        return word == "I" || Regex.IsMatch(expected, $@"\b{Regex.Escape(word)}\b");
    }

    private static bool EndsASentence(string? line) => line?.TrimEnd() is { Length: > 0 } text && text[^1] is '.' or '?' or '!';

    /// <summary>Whether a line's first letter (after a list marker) is a capital; null for none.</summary>
    private static bool? FirstLetterCase(string? line)
    {
        string text = ListItem().Replace(line ?? string.Empty, string.Empty);
        foreach (char ch in text)
        {
            if (char.IsLetter(ch))
            {
                return char.IsUpper(ch);
            }
        }

        return null;
    }

    /// <summary>One letter a line: B blank, L list item, T text.</summary>
    private static string Shape(string text) => string.Concat(text.Trim().Replace("\r\n", "\n").Split('\n')
        .Select(line => line.Trim().Length == 0 ? 'B' : ListItem().IsMatch(line) ? 'L' : 'T'));

    private static int Blocks(string shape) => shape.Split('B', StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool IsWorkChat(PerAppCase c) => AppKinds.Of(c.App, c.Title) == AppKind.WorkChat;

    /// <summary>A greeting alone on the first line, then a blank line.</summary>
    private static bool StartsLikeAnEmail(string text) =>
        text.Trim().Split('\n') is [var first, "", ..] && first.TrimEnd().EndsWith(',') && first.Split(' ').Length <= 5;

    /// <summary>A blank line, then the sign-off alone ("Thanks," and the name under it, or "Thanks!").</summary>
    private static bool EndsLikeAnEmail(string text)
    {
        string[] lines = text.Trim().Split('\n');
        return lines.Length >= 3 &&
               ((lines[^2].TrimEnd().EndsWith(',') && lines[^3].Length == 0) || (lines[^2].Length == 0 && lines[^1].Split(' ').Length <= 3));
    }

    private static bool EndsInFullStop(string line)
    {
        string trimmed = line.TrimEnd();
        return trimmed.EndsWith('.') && !trimmed.EndsWith("..", StringComparison.Ordinal) && !trimmed.EndsWith("etc.", StringComparison.Ordinal);
    }

    private static List<string> Tokens(string text)
    {
        string expanded = Contraction().Replace(text.ToLowerInvariant().Replace('’', '\''), match => match.Value switch
        {
            "n't" => " not",
            "'ll" => " will",
            "'m" => " am",
            "'re" => " are",
            "'ve" => " have",
            "'d" => " would",
            _ => match.Value,
        });
        expanded = expanded.Replace("can not", "cannot").Replace("wo not", "will not");
        return Word().Matches(expanded).Select(match => match.Value).ToList();
    }

    private static int EditDistance(List<string> a, List<string> b)
    {
        int[,] d = new int[a.Count + 1, b.Count + 1];
        for (int i = 0; i <= a.Count; i++)
        {
            d[i, 0] = i;
        }

        for (int j = 0; j <= b.Count; j++)
        {
            d[0, j] = j;
        }

        for (int i = 1; i <= a.Count; i++)
        {
            for (int j = 1; j <= b.Count; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }
        }

        return d[a.Count, b.Count];
    }

    private static int Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    // ---- Report

    private static string Report(List<PerAppRow> rows, string engine)
    {
        StringBuilder sb = new();
        sb.AppendLine($"# Per-app bench: {engine}");
        sb.AppendLine();
        sb.AppendLine($"{rows.Count} cases. Pass (words and layout): **{rows.Count(r => r.Pass)} of {rows.Count}**. " +
                      $"Words {rows.Count(r => r.WordsOk)}, layout {rows.Count(r => r.LayoutOk)} (exact {rows.Count(r => r.LayoutExact)}).");
        sb.AppendLine();
        sb.AppendLine("Latency is from the end of speech to the text (no speech recognition: the bench starts from its words), " +
                      "with the model primed at the hotkey press while the message is spoken. Every case is in a different kind of " +
                      "app from the one before, the slowest order for the model's cache.");
        sb.AppendLine();
        sb.AppendLine("Pass: the target's words (one in twenty may differ; punctuation, case and contractions never count) and its list " +
                      "items, exactly. Email: the greeting and sign-off lines as in the target, paragraphs within one. Document: " +
                      "paragraphs within one. Chat: no blank line, no line ending in a full stop, lines within one fewer or two more. " +
                      "AI and other apps: the target's exact lines.");
        sb.AppendLine();
        sb.AppendLine("| kind | cases | pass | words | layout | p50 ms | p95 ms | max ms | primed in time |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (IGrouping<string, PerAppRow> g in rows.GroupBy(r => r.AppKind))
        {
            double[] ms = g.Select(r => (double)r.Ms).OrderBy(x => x).ToArray();
            sb.AppendLine($"| {g.Key} | {g.Count()} | {g.Count(r => r.Pass)} | {g.Count(r => r.WordsOk)} | {g.Count(r => r.LayoutOk)} | " +
                          $"{Pct(ms, 0.5):0} | {Pct(ms, 0.95):0} | {ms[^1]:0} | {g.Count(r => r.PrimedInTime)} |");
        }

        double[] all = rows.Select(r => (double)r.Ms).OrderBy(x => x).ToArray();
        sb.AppendLine($"| **all** | {rows.Count} | {rows.Count(r => r.Pass)} | {rows.Count(r => r.WordsOk)} | {rows.Count(r => r.LayoutOk)} | " +
                      $"{Pct(all, 0.5):0} | {Pct(all, 0.95):0} | {all[^1]:0} | {rows.Count(r => r.PrimedInTime)} |");

        if (rows.Any(r => r.Style is not null))
        {
            sb.AppendLine();
            sb.AppendLine("By Style (pass needs the Style's own check too: Formal no contraction or casual word and the ending kept; " +
                          "Casual, Very casual and Excited no line ending in a full stop; Casual the first capital, Very casual each " +
                          "line's first letter as in the target; Excited its exclamation marks, one fewer at most):");
            sb.AppendLine();
            sb.AppendLine("| style | cases | pass | style check | p50 ms |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (IGrouping<string?, PerAppRow> g in rows.GroupBy(r => r.Style))
            {
                double[] ms = g.Select(r => (double)r.Ms).OrderBy(x => x).ToArray();
                sb.AppendLine($"| {g.Key ?? "kind's own"} | {g.Count()} | {g.Count(r => r.Pass)} | {g.Count(r => r.StyleOk)} | {Pct(ms, 0.5):0} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Cases that do not pass");
        foreach (PerAppRow r in rows.Where(r => !r.Pass))
        {
            sb.AppendLine();
            sb.AppendLine($"**{r.Id}** ({r.AppKind}, {r.App}{(r.Title is null ? "" : ", browser")}; words {(r.WordsOk ? "ok" : $"off by {r.Distance}")}, " +
                          $"layout {(r.LayoutOk ? "ok" : "off")}{(r.Style is null ? "" : $", style {(r.StyleOk ? "ok" : "off")}")}, {r.Ms} ms{(r.Engine is null ? "" : $"; {r.Engine}")})");
            sb.AppendLine("```text");
            sb.AppendLine(r.Output);
            sb.AppendLine("```");
            sb.AppendLine("want:");
            sb.AppendLine("```text");
            sb.AppendLine(r.Expected);
            sb.AppendLine("```");
        }

        sb.AppendLine();
        sb.AppendLine("## Every output, verbatim");
        foreach (IGrouping<string, PerAppRow> message in rows.GroupBy(r => r.Message))
        {
            sb.AppendLine();
            sb.AppendLine($"### {message.Key}");
            sb.AppendLine();
            sb.AppendLine($"Spoken: \"{message.First().Spoken}\"");
            foreach (PerAppRow r in message)
            {
                sb.AppendLine();
                sb.AppendLine($"{r.AppKind}{(r.Style is null ? "" : $", {r.Style}")} ({r.App}{(r.Title is null ? "" : $", \"{r.Title}\"")}, {(r.Pass ? "pass" : "FAIL")}, {r.Ms} ms):");
                sb.AppendLine("```text");
                sb.AppendLine(r.Output);
                sb.AppendLine("```");
            }
        }

        return sb.ToString();
    }

    private static double Pct(double[] sorted, double p) => sorted.Length == 0 ? 0 : sorted[(int)Math.Min(sorted.Length - 1, Math.Floor(p * sorted.Length))];

    [GeneratedRegex(@"^\s*(?:\d+[.)]|[-*•])\s")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"n't|'ll|'m|'re|'ve|'d")]
    private static partial Regex Contraction();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();

    /// <summary>What Formal never leaves: a contraction ("it's" included, a name's "'s" not) or a casual word.</summary>
    [GeneratedRegex(@"(?i)\b(?:\w+n['’]t|\w+['’](?:ll|re|ve|m|d)|(?:it|that|there|here|what|who|where|how|he|she)['’]s|yeah|yep|yup|nope|gonna|wanna|gotta)\b")]
    private static partial Regex InformalWord();
}

internal sealed class PerAppCase
{
    public string Id { get; set; } = "";
    public string Message { get; set; } = "";
    public string AppKind { get; set; } = "";
    public string App { get; set; } = "";
    public string? Title { get; set; }
    public string Text { get; set; } = "";
    public string Expected { get; set; } = "";

    /// <summary>The Style the case's kind of app is set to (ticket 56); null for the kind's default.</summary>
    public string? Style { get; set; }
}

internal sealed record PerAppRow(
    string Id, string Message, string AppKind, string App, string? Title, string Spoken, string Expected, string Output, long Ms,
    bool PrimedInTime, bool WordsOk, int Distance, bool LayoutOk, bool LayoutExact, bool Pass)
{
    /// <summary>Offline: what Smart cleanup and Mend did, from the engine record.</summary>
    public string? Engine { get; init; }

    public string? Style { get; init; }

    public bool StyleOk { get; init; } = true;
}
