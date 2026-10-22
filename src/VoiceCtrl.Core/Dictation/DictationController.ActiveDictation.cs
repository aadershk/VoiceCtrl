namespace VoiceCtrl.Core.Dictation;

public sealed partial class DictationController
{
    private sealed class ActiveDictation(DateTimeOffset startedAt, bool startedByHold, string? appName)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;

        public bool StartedByHold { get; } = startedByHold;

        /// <summary>The app the Dictation is for, read when it starts (the Overlay never takes focus).</summary>
        public string? AppName { get; } = appName;

        public bool IsListening { get; set; } = true;

        /// <summary>Hands-free: the recording stays on after the keys are released, until the
        /// next press (VED-74).</summary>
        public bool IsLocked { get; init; }

        /// <summary>Set once Transcribing has run past <see cref="SlowThreshold"/>.</summary>
        public bool IsSlow { get; set; }

        /// <summary>True for a rewrite by voice: the clip is an instruction for some text, not a Dictation.</summary>
        public bool IsRewrite { get; init; }

        /// <summary>True once a rewrite turned out to be an Email request and its email is being
        /// written (ticket 57).</summary>
        public bool IsWriting { get; set; }

        /// <summary>Set when this is an Overlay Retry of a kept Failed Dictation.</summary>
        public FailedDictation? RetryOf { get; init; }

        /// <summary>The clock for "Taking longer than usual", kept so it can be dropped on Cancel.</summary>
        public IDisposable? SlowTimer { get; set; }

        public CancellationTokenSource Cancellation { get; } = new();

        /// <summary>The recorded clip once recording has stopped, so a Cancel while Transcribing
        /// can keep it (ADR-0004).</summary>
        public byte[]? Audio { get; set; }

        public TimeSpan AudioLength { get; set; }

        /// <summary>True when the recording ended because it reached the cap.</summary>
        public bool HitLimit { get; set; }

        /// <summary>The "a minute left" and "limit reached" clocks, dropped when recording ends.</summary>
        public IDisposable[]? LimitTimers { get; set; }

        public void DisposeLimitTimers()
        {
            foreach (IDisposable timer in LimitTimers ?? [])
            {
                timer.Dispose();
            }

            LimitTimers = null;
        }
    }
}
