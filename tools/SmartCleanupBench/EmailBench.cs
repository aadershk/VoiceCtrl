using System.Diagnostics;
using System.Text;
using System.Text.Json;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.EmailRequests;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Status;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Tools.SmartCleanupBench;

/// <summary>
/// The Email request bench (ticket 57): Rewrite key instructions said with nothing selected in
/// Outlook, each a request for a new email or an edit of the last Dictation. It reports how often each
/// is taken for the other, and every email written. Cases labelled "dictated" are left out: plain
/// dictation is never read for requests.
///
///   SmartCleanupBench --email cases.json                                   the words alone
///   SmartCleanupBench --email cases.json --models &lt;packs root&gt; [--speech &lt;models root&gt; --audio &lt;wav folder&gt;]
///                                                                          Offline: from the words, or heard
///                                                                          from a recording by the speech model
///   SmartCleanupBench --email cases.json --online &lt;wav folder&gt;           Online: Gemini hears a recording
/// </summary>
internal static class EmailBench
{
    private const string App = "outlook";
    private const string YourName = "Alex Kim";

    public static async Task<int> RunAsync(Dictionary<string, string> options)
    {
        string casesPath = options["email"];
        List<EmailCase> cases = JsonSerializer.Deserialize<List<EmailCase>>(
            File.ReadAllText(casesPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
            .Where(c => c.Label is "request" or "edit").ToList();

        string engine;
        List<EmailRow> rows;
        if (options.TryGetValue("online", out string? onlineAudio))
        {
            OnlineBench online = OnlineBench.From(options);
            engine = online.Describe + ": hears the instruction, writes the body";
            rows = await RunOnlineAsync(cases, onlineAudio, online);
        }
        else if (options.TryGetValue("models", out string? packs))
        {
            string? audio = options.GetValueOrDefault("audio");
            engine = audio is null ? "Offline from the words (Qwen3-4B writes)" : "Offline from a recording (Parakeet hears the instruction, Qwen3-4B writes)";
            rows = await RunOfflineAsync(cases, packs, options.GetValueOrDefault("speech"), audio);
        }
        else
        {
            engine = "Request or edit, on the words as written";
            rows = cases.Select(c => Row(c, c.Text, EmailRequest.FromInstruction(c.Text), null, 0)).ToList();
        }

        string report = Report(rows, engine, Path.GetFileName(casesPath));
        Console.WriteLine(report);
        if (options.TryGetValue("out", out string? outPath))
        {
            File.WriteAllText(outPath, report);
            File.WriteAllText(Path.ChangeExtension(outPath, ".rows.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }

        return 0;
    }

    private static PromptContext Context() =>
        GeminiPrompt.ContextFor(null, App, new PromptDefaults(CleanupLevels.Standard, ToneAwareness: true, [], [], Styles: Styles()));

    private static StyleSettings Styles() => new() { YourName = YourName };

    private static async Task<List<EmailRow>> RunOfflineAsync(List<EmailCase> cases, string packsRoot, string? speechRoot, string? audioFolder)
    {
        SettingsStore settings = SettingsStore.InMemory(new UserSettings { Transcription = new TranscriptionSection { SmartCleanupTier = SmartCleanupTier.Better } });
        var packs = new SmartCleanupPackManager(settings, packsRoot, (_, _, _) => Task.CompletedTask, () => Task.CompletedTask, () => TimeSpan.Zero);
        using var smartCleanup = new SmartCleanupService(settings, packs, 8);
        if (!smartCleanup.IsAvailable)
        {
            throw new InvalidOperationException($"No Smart cleanup pack under {packsRoot}.");
        }

        AppConfig config = ConfigLoader.Load(new UserSettings(), string.Empty);
        using var client = new LocalTranscriptionClient(
            config,
            modelsRoot: speechRoot,
            foregroundProcessName: () => App,
            builtInVocabulary: () => BuiltInVocabulary.Shared,
            styles: Styles);
        var writer = new EmailWriter(
            Context, Styles, PersonalizationStore.Empty, () => TranscriptionModePreference.Offline,
            new NoOnline(), new LocalEmailBodyWriter(smartCleanup), new ApiKeyHealth());

        Stopwatch load = Stopwatch.StartNew();
        await Task.WhenAll(audioFolder is null ? Task.CompletedTask : client.PrewarmConnectionAsync(), writer.PrewarmAsync());
        Console.WriteLine($"Models loaded and primed in {load.Elapsed.TotalSeconds:0.0} s.");

        List<EmailRow> rows = [];
        foreach (EmailCase c in cases)
        {
            // The Rewrite key press primes the writer while the user speaks.
            Task prime = writer.PrewarmAsync();
            await Task.WhenAny(prime, Task.Delay(TimeSpan.FromSeconds(Math.Max(1.5, Words(c.Text) / 2.5))));

            Stopwatch clock = Stopwatch.StartNew();
            string transcript = audioFolder is null
                ? c.Text
                : await client.TranscribeInstructionAsync(File.ReadAllBytes(Path.Combine(audioFolder, c.Id + ".wav")), CancellationToken.None) ?? string.Empty;
            long transcribeMs = clock.ElapsedMilliseconds;
            await prime;

            rows.Add(await WriteAsync(c, transcript, writer, transcribeMs));
            Console.WriteLine($"{c.Id}: {rows[^1].Verdict} ({rows[^1].TranscribeMs} + {rows[^1].WriteMs} ms)");
        }

        return rows;
    }

    private static async Task<List<EmailRow>> RunOnlineAsync(List<EmailCase> cases, string audioFolder, OnlineBench online)
    {
        VoiceCtrl.Core.Rewrite.IRewriteClient client = online.Rewriter();
        using var writer = new EmailWriter(
            Context, Styles, PersonalizationStore.Empty, () => TranscriptionModePreference.Online,
            online.EmailWriter(), new NoOnline(), new ApiKeyHealth());
        bool gemini = online.Endpoint.Provider == VoiceCtrl.Core.Online.OnlineProvider.Gemini;
        TimeSpan pace = TimeSpan.FromSeconds(gemini ? 7 : 1);

        List<EmailRow> rows = [];
        foreach (EmailCase c in cases)
        {
            // A free key allows about ten requests a minute: pace the calls, and try a refused one again.
            string transcript = string.Empty;
            long ms = 0;
            for (int attempt = 1; attempt <= 4; attempt++)
            {
                await Task.Delay(attempt == 1 ? pace : TimeSpan.FromSeconds(gemini ? 30 : 5));
                Stopwatch clock = Stopwatch.StartNew();
                try
                {
                    transcript = await client.HearAsync(File.ReadAllBytes(Path.Combine(audioFolder, c.Id + ".wav")), CancellationToken.None) ?? string.Empty;
                    ms = clock.ElapsedMilliseconds;
                    break;
                }
                catch (Exception ex)
                {
                    transcript = $"[error: {ex.Message.Split('.')[0]}]";
                    Console.WriteLine($"{c.Id}: attempt {attempt} refused ({ex.Message.Split('.')[0]})");
                }
            }

            rows.Add(await WriteAsync(c, transcript, writer, ms, pause: pace));
            Console.WriteLine($"{c.Id}: {rows[^1].Verdict} ({rows[^1].TranscribeMs} + {rows[^1].WriteMs} ms)");
        }

        return rows;
    }

    private static async Task<EmailRow> WriteAsync(EmailCase c, string transcript, EmailWriter writer, long transcribeMs, TimeSpan pause = default)
    {
        EmailRequest? request = writer.ForInstruction(transcript);
        if (request is null)
        {
            return Row(c, transcript, null, null, transcribeMs);
        }

        // Online, a pause first keeps a free key under its requests-a-minute limit; it is not timed.
        await Task.Delay(pause);

        Stopwatch clock = Stopwatch.StartNew();
        (string? email, string? refused, string? body) = await writer.WriteWithReasonAsync(request, CancellationToken.None);
        EmailRow row = Row(c, transcript, request, email, transcribeMs) with
        {
            WriteMs = clock.ElapsedMilliseconds,
            RefusedBody = email is null ? body : null,
        };
        return c.Label == "request" ? row with { Verdict = email is null ? $"request, refused ({refused})" : "request, written" } : row;
    }

    private static EmailRow Row(EmailCase c, string transcript, EmailRequest? request, string? email, long transcribeMs)
    {
        bool isRequest = c.Label == "request";
        bool recognised = request is not null;
        bool recipientOk = !isRequest || !recognised || SameRecipient(c.Recipient, request!.Recipient);
        string verdict = (isRequest, recognised) switch
        {
            (true, true) => "request, recognised",
            (true, false) => "MISSED request (taken for an edit)",
            (false, true) => "EDIT taken for a request",
            (false, false) => "edit, kept",
        };

        return new EmailRow(c.Id, c.Label, c.Hard, c.Text, transcript, recognised, c.Recipient, request?.Recipient, recipientOk, email, verdict, transcribeMs);
    }

    /// <summary>"the team" and "team", "Dr. Patel" and "Dr Patel" are the same recipient.</summary>
    private static bool SameRecipient(string? want, string? got) =>
        Normal(want) == Normal(got) || (got is null && IsRole(want));

    /// <summary>"the neighbours", "my landlord": a role, which the email greets as "Hello,".</summary>
    private static bool IsRole(string? want) =>
        want is not null && (want.StartsWith("the ", StringComparison.OrdinalIgnoreCase) || want.StartsWith("my ", StringComparison.OrdinalIgnoreCase)) &&
        Normal(want) is not ("team" or "all" or "everyone");

    private static string Normal(string? who) =>
        (who ?? string.Empty).ToLowerInvariant().Replace(".", string.Empty).Replace("the ", string.Empty).Replace("whole ", string.Empty).Replace("entire ", string.Empty).Replace("everybody", "everyone").Trim() switch
        {
            // A group other than a team or everyone is greeted as "all".
            "board" or "group" or "class" or "committee" or "staff" or "department" => "all",
            // "the marketing team" is greeted "Hi team,".
            var other when other.EndsWith(" team", StringComparison.Ordinal) => "team",
            var other => other,
        };

    private static int Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string Report(List<EmailRow> rows, string engine, string casesName)
    {
        List<EmailRow> requests = rows.Where(r => r.Label == "request").ToList();
        List<EmailRow> edits = rows.Where(r => r.Label != "request").ToList();
        int missed = requests.Count(r => !r.Recognised);
        int wrongEdits = edits.Count(r => r.Recognised);
        int written = requests.Count(r => r.Recognised && r.Email is not null);
        int fellBack = requests.Count(r => r.Recognised && r.Email is null && r.WriteMs > 0);

        StringBuilder sb = new();
        sb.AppendLine($"# Email request bench: {engine}");
        sb.AppendLine();
        sb.AppendLine($"Cases: `{casesName}`, {requests.Count} requests and {edits.Count} edits, each said with the Rewrite key and " +
                      $"nothing selected in Outlook (Email Style Formal, Your name \"{YourName}\"). Dictated emails are not run: plain " +
                      "dictation is never read for requests.");
        sb.AppendLine();
        sb.AppendLine("| | cases | wrong | rate |");
        sb.AppendLine("|---|---|---|---|");
        sb.AppendLine($"| edit taken for a request (an email written instead) | {edits.Count} | {wrongEdits} | {Rate(wrongEdits, edits.Count)} |");
        sb.AppendLine($"| request taken for an edit (the last Dictation rewritten instead) | {requests.Count} | {missed} | {Rate(missed, requests.Count)} |");
        sb.AppendLine($"| request's recipient wrong | {requests.Count(r => r.Recognised)} | {requests.Count(r => r.Recognised && !r.RecipientOk)} | |");
        if (rows.Any(r => r.WriteMs > 0))
        {
            double[] ms = requests.Where(r => r.WriteMs > 0).Select(r => (double)r.WriteMs).OrderBy(x => x).ToArray();
            sb.AppendLine($"| recognised request written / refused (nothing pasted) | {written + fellBack} | {fellBack} | write p50 {Pct(ms, 0.5):0} ms, p95 {Pct(ms, 0.95):0} ms, max {ms[^1]:0} ms |");
        }

        sb.AppendLine();
        sb.AppendLine("Hard cases (marked by the set's writer): " +
                      $"{rows.Count(r => r.Hard)}, wrong {rows.Count(r => r.Hard && (r.Recognised != (r.Label == "request")))}.");

        sb.AppendLine();
        sb.AppendLine("## Wrong");
        foreach (EmailRow r in rows.Where(r => r.Recognised != (r.Label == "request") || !r.RecipientOk))
        {
            sb.AppendLine();
            sb.AppendLine($"**{r.Id}** ({r.Verdict}{(r.RecipientOk ? "" : $", recipient {r.Recipient ?? "none"} not {r.WantRecipient ?? "none"}")}): \"{r.Transcript}\"");
        }

        sb.AppendLine();
        sb.AppendLine("## Every email written");
        foreach (EmailRow r in rows.Where(r => r.Recognised))
        {
            sb.AppendLine();
            sb.AppendLine($"**{r.Id}** ({r.Verdict}, {r.WriteMs} ms): \"{r.Transcript}\"");
            sb.AppendLine("```text");
            sb.AppendLine(r.Email ?? (r.RefusedBody is null ? "(not written)" : "(refused; the body was:)\n" + r.RefusedBody));
            sb.AppendLine("```");
        }

        return sb.ToString();
    }

    private static string Rate(int wrong, int total) => total == 0 ? "-" : $"{100.0 * wrong / total:0.0}%";

    private static double Pct(double[] sorted, double p) => sorted.Length == 0 ? 0 : sorted[(int)Math.Min(sorted.Length - 1, Math.Floor(p * sorted.Length))];

    /// <summary>The writer for the Mode the bench does not use.</summary>
    private sealed class NoOnline : IEmailBodyWriter
    {
        public bool IsReady => false;

        public Task<string?> WriteBodyAsync(EmailRequest request, IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }
}

internal sealed class EmailCase
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Recipient { get; set; }
    public bool Hard { get; set; }
}

internal sealed record EmailRow(
    string Id, string Label, bool Hard, string Spoken, string Transcript, bool Recognised, string? WantRecipient, string? Recipient,
    bool RecipientOk, string? Email, string Verdict, long TranscribeMs)
{
    public long WriteMs { get; init; }

    public string? RefusedBody { get; init; }
}
