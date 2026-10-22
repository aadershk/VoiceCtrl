using System.Net.Http;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Online;

/// <summary>
/// A provider's primary model, then its backup (ticket 60). The primary gets a short "first answer"
/// deadline, so a model that hangs (Gemini's flash models hung past 40 s on 2026-10-21; super-vl
/// stalled 29 s once in 12 calls) costs seconds, not the whole request timeout. The backup gets the
/// request's full timeout. A refused key is not retried: the backup uses the same key.
/// </summary>
public static class ModelAttempts
{
    /// <summary>10 s for a short clip, plus a quarter second per second of audio: a 4-minute piece
    /// gets 70 s. Gemini 3.6 flash answered short clips in 11-15 s, NVIDIA's omni in about 1 s.</summary>
    public static TimeSpan AudioDeadline(int wavByteCount) =>
        TimeSpan.FromSeconds(10 + wavByteCount / 32_000.0 * 0.25);

    /// <summary>5 s for a short text, plus a second per 250 characters (a page gets about 20 s).
    /// super-vl and ultra answered short text in 0.5-2 s.</summary>
    public static TimeSpan TextDeadline(int characters) =>
        TimeSpan.FromSeconds(5 + characters / 250.0);

    /// <summary>Runs <paramref name="attempt"/> with each model in turn. Every model but the last is
    /// given <paramref name="firstAnswer"/>; a model that misses it, is busy or can't be reached hands
    /// over to the next. The last model's failure is thrown as it was.</summary>
    public static async Task<T> RunAsync<T>(
        IReadOnlyList<string> models,
        TimeSpan firstAnswer,
        Func<string, CancellationToken, Task<T>> attempt,
        string job,
        CancellationToken cancellationToken)
    {
        for (int i = 0; ; i++)
        {
            string model = models[i];
            if (i == models.Count - 1)
            {
                return await attempt(model, cancellationToken).ConfigureAwait(false);
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(firstAnswer);
            try
            {
                return await attempt(model, deadline.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsWorthTheBackup(ex))
            {
                string why = ex is OperationCanceledException ? $"no answer in {firstAnswer.TotalSeconds:0} s" : OnlineFailures.Describe(ex);
                SimpleFileLogger.LogInfo($"{job}: {model} {why}; trying {models[i + 1]}");
            }
        }
    }

    private static bool IsWorthTheBackup(Exception ex) => ex switch
    {
        OnlineServiceException service => OnlineFailures.IsTransient(service),
        _ => ex is OperationCanceledException or HttpRequestException,
    };
}
