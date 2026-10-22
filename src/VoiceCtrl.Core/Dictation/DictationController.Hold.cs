namespace VoiceCtrl.Core.Dictation;

/// <summary>
/// Hold-to-Talk (VED-74): the Trigger Key's press and release. Recording starts on the press,
/// a hold under <see cref="TapThreshold"/> is a silent tap, and two quick presses lock the
/// recording on hands-free. Kept apart so the controller stays under the 500-line limit.
/// </summary>
public sealed partial class DictationController
{
    /// <summary>
    /// Trigger Key press (Hold-to-Talk): starts a Dictation at once from any state, even with the
    /// Overlay hidden, unless one is already Listening or Transcribing. A second press within
    /// <see cref="TapThreshold"/> of a quick tap's start locks the recording on (hands-free); the
    /// next press while locked ends it.
    /// </summary>
    public Task HoldStart()
    {
        if (_active is { IsListening: false })
        {
            return PillPressed();
        }

        if (_active is { IsListening: true, IsLocked: true, IsRewrite: false } locked && IsListeningView)
        {
            return StopAsync(locked);
        }

        if (IsListeningView || View.Phase == OverlayPhase.Transcribing)
        {
            return Task.CompletedTask;
        }

        bool lockOn = _lastTapStartedAt is { } tapStart && _collaborators.Timer.UtcNow - tapStart <= TapThreshold;
        _lastTapStartedAt = null;
        return StartDictation(startedByHold: true, isLocked: lockOn);
    }

    /// <summary>Trigger Key release: stops the Dictation its hold started, and only that one. A
    /// hold under <see cref="TapThreshold"/> was a tap: the recording is dropped without a sound or
    /// a message. A locked recording ignores the release.</summary>
    public Task HoldEnd() =>
        IsListeningView && _active is { StartedByHold: true, IsRewrite: false, IsLocked: false } dictation
            ? EndHold(dictation)
            : Task.CompletedTask;

    /// <summary>Ends a hold: a tap is dropped quietly, a real hold is stopped and transcribed.</summary>
    private Task EndHold(ActiveDictation dictation)
    {
        if (_collaborators.Timer.UtcNow - dictation.StartedAt >= TapThreshold)
        {
            return StopAsync(dictation);
        }

        if (!dictation.IsRewrite)
        {
            _lastTapStartedAt = dictation.StartedAt;
        }

        Discard(afterCancel: false);
        return Task.CompletedTask;
    }

}
