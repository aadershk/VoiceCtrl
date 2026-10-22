using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Dictation;

/// <summary>The recorder-facing helpers: dropping a cancelled recording and throttling the level
/// stream. Kept apart so the controller stays under the 500-line limit.</summary>
public sealed partial class DictationController
{
    /// <summary>A cancelled recording's audio is stopped and dropped; a failure here has nothing
    /// left to report to, so it is only logged.</summary>
    private async Task DiscardRecordingAsync()
    {
        try
        {
            await _collaborators.Recorder.StopAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("DiscardRecording", ex);
        }
    }

    /// <summary>
    /// Throttles the recorder's level to the display's rate. A level that arrives too soon after
    /// the last one is dropped rather than queued: the page smooths per frame, so only the newest
    /// value matters, and a queue would replay as a laggy meter.
    /// </summary>
    private void OnRecorderLevel(float level)
    {
        if (!_isListening)
        {
            return;
        }

        long now = _collaborators.Timer.UtcNow.UtcTicks;
        long last = Interlocked.Read(ref _lastLevelTicks);
        if (now - last < MinLevelInterval.Ticks
            || Interlocked.CompareExchange(ref _lastLevelTicks, now, last) != last)
        {
            return;
        }

        LevelChanged?.Invoke(Math.Clamp(level, 0f, 1f));
    }
}
