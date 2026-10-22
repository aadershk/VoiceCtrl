using System.Diagnostics;
using System.Text.Json;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Rewrite;

namespace VoiceCtrl.Tools.SmartCleanupBench;

/// <summary>
/// Rewrite by voice Online (ticket 60): each case's instruction is a recording (Windows' own voice,
/// <c>.scratch/v4-ship/online-providers/speak-rewrite.ps1</c>), sent through the app's own client for
/// the provider, so the provider hears it as it would in the app. Judged by the same checks.
/// <code>SmartCleanupBench --rewrite-cases cases.json --online &lt;wav folder&gt; [--provider nvidia] [--out report.md]</code>
/// </summary>
internal static partial class RewriteBench
{
    public static async Task<int> RunOnlineAsync(string casesPath, string audioFolder, OnlineBench online, string? outPath)
    {
        JsonSerializerOptions json = new() { PropertyNameCaseInsensitive = true };
        List<RewriteCase> cases = JsonSerializer.Deserialize<List<RewriteCase>>(File.ReadAllText(casesPath), json)!;
        IRewriteClient client = online.Rewriter();
        bool gemini = online.Endpoint.Provider == OnlineProvider.Gemini;

        List<RewriteRow> rows = [];
        foreach (RewriteCase c in cases)
        {
            byte[] wav = File.ReadAllBytes(Path.Combine(audioFolder, c.Id + ".wav"));
            RewriteRow? row = null;
            for (int attempt = 1; attempt <= 4 && row is null; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt == 1 ? (gemini ? 7 : 1) : (gemini ? 30 : 5)));
                Stopwatch timer = Stopwatch.StartNew();
                try
                {
                    RewriteOutcome outcome = await client.RewriteAsync(wav, c.Text, CancellationToken.None);
                    long ms = timer.ElapsedMilliseconds;
                    row = outcome.Text is { } output
                        ? new RewriteRow(c.Id, c.Kind, c.Instruction, c.Text, output, Check(c, output).Count == 0, Check(c, output), ms)
                        : new RewriteRow(c.Id, c.Kind, c.Instruction, c.Text, null, false, [outcome.Kind.ToString()], ms);
                }
                catch (Exception ex) when (attempt < 4)
                {
                    Console.WriteLine($"{c.Id}: attempt {attempt} refused ({ex.Message.Split('.')[0]})");
                }
                catch (Exception ex)
                {
                    row = new RewriteRow(c.Id, c.Kind, c.Instruction, c.Text, null, false, [$"error: {ex.Message.Split('.')[0]}"], 0);
                }
            }

            rows.Add(row!);
            Console.WriteLine($"{c.Id}: {(row!.Passed ? "pass" : string.Join("; ", row.Failures))} ({row.Ms} ms)");
        }

        string report = Report(rows, 0, online.Describe);
        Console.WriteLine(report);
        if (outPath is not null)
        {
            File.WriteAllText(outPath, report);
            File.WriteAllText(Path.ChangeExtension(outPath, ".rows.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }

        return 0;
    }
}
