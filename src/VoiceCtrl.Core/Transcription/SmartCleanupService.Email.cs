using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.EmailRequests;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Personalization;

namespace VoiceCtrl.Core.Transcription;

/// <summary>The local model writing an email from an Email request (ticket 57). Kept apart so the
/// service stays under the 500-line limit.</summary>
public sealed partial class SmartCleanupService
{
    /// <summary>
    /// Wait for an email body written from a request of <paramref name="words"/> words (ticket 57): it
    /// writes far more than it reads, up to about 80 words for a short request, at the decoding speeds
    /// above, plus a share per word of the request, since a detailed request gets a longer email.
    /// </summary>
    public static TimeSpan EmailTimeoutFor(SmartCleanupTier tier, int words)
    {
        (double baseSeconds, double secondsPerWord) = tier == SmartCleanupTier.Standard ? (10.0, 0.25) : (20.0, 0.5);
        return TimeSpan.FromSeconds(Math.Min(60, baseSeconds + secondsPerWord * words));
    }

    /// <summary>
    /// The local model's reply for the body of an email written from <paramref name="request"/> (ticket
    /// 57), or null when there is no model, it is too slow or it fails. The caller cleans and checks the
    /// reply. Never throws but for cancellation, and does not count toward giving up on Smart cleanup.
    /// </summary>
    internal async Task<string?> WriteEmailBody(EmailRequest request, IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken)
    {
        if (ModelNow() is not { } model)
        {
            return null;
        }

        // Reading the cleanup rules is not this call's wait.
        await WaitForPrimeAsync(cancellationToken).ConfigureAwait(false);
        int words = request.Words.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        ModelRequest modelRequest = new(EmailText.System(dictionary), EmailText.ExamplePairs, EmailText.User(request.Words, request.Recipient, request.Style), MaxTokens: 320);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(EmailTimeoutFor(_settings.Current.Transcription.SmartCleanupTier, words));
        try
        {
            string? reply = await model.CompleteAsync(modelRequest, timeout.Token).ConfigureAwait(false);
            ResetIdleTimer();
            return reply;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SimpleFileLogger.LogInfo("Email request: the language model timed out; the request is typed as said.");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SimpleFileLogger.LogError("EmailRequestWrite", ex);
            return null;
        }
    }

    /// <summary>Has the model read the email rules while the user speaks into an email app, after the
    /// cleanup rules (which every Dictation there needs first). Best-effort: never throws.</summary>
    internal async Task PrimeEmail(IReadOnlyList<DictionaryEntry> dictionary, CancellationToken cancellationToken)
    {
        try
        {
            if (ModelNow() is not { } model)
            {
                return;
            }

            await WaitForPrimeAsync(cancellationToken).ConfigureAwait(false);
            ModelRequest request = new(EmailText.System(dictionary), EmailText.ExamplePairs, EmailText.User("Write an email.", null, Style.Formal), MaxTokens: 1);
            await model.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
            ResetIdleTimer();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SimpleFileLogger.LogError("EmailRequestPrime", ex);
        }
    }
}
