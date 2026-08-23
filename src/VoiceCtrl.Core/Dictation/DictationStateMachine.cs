namespace VoiceCtrl.Core.Dictation;

public enum DictationState
{
    Idle,
    Recording,
    Processing,
    Error,
}

/// <summary>What a toggle request resolved to, so the caller knows which pipeline to run.</summary>
public enum DictationAction
{
    None,
    Start,
    Stop,
}

/// <summary>
/// The overlay's state machine, kept free of WPF types so its re-entrancy rules can be unit
/// tested. Those rules carry more weight than they used to: the double-tap hotkey can now fire
/// again while a previous start or stop is still in flight, where the old mouse-click path was
/// naturally rate-limited by how fast a person can hit a 48px target twice.
///
/// The gate is <see cref="IsTransitioning"/> rather than a plain "am I processing" flag, because
/// the window that needs protecting is wider than the Processing state. It also has to cover the
/// device-startup time inside WasapiCapture, and the auto-hide delay on a transient error
/// message: during that delay the state is Error, and a recording started by an impatient second
/// double-tap would be killed moments later by the pending hide.
/// </summary>
public sealed class DictationStateMachine
{
    public DictationState State { get; private set; } = DictationState.Idle;

    public string? StatusMessage { get; private set; }

    /// <summary>True while a start, stop or cancel that a caller claimed has not yet finished.</summary>
    public bool IsTransitioning { get; private set; }

    /// <summary>Settled mid-recording, so a cancel is meaningful and a stop is worth running.
    /// Read from the keyboard hook to decide whether an Esc press is even worth dispatching.</summary>
    public bool IsRecording => State == DictationState.Recording && !IsTransitioning;

    /// <summary>
    /// Resolves a hotkey or click into an action and, when it resolves to one, claims the
    /// transition in the same call. Deciding and claiming have to happen together: a two-call
    /// "may I?" then "I am" shape leaves a gap for a second toggle to slip through.
    /// </summary>
    public DictationAction RequestToggle()
    {
        if (IsTransitioning)
        {
            return DictationAction.None;
        }

        DictationAction action = State == DictationState.Recording
            ? DictationAction.Stop
            : DictationAction.Start;

        IsTransitioning = true;
        return action;
    }

    /// <summary>
    /// Esc. Only meaningful mid-recording: anywhere else there is nothing left to throw away, and
    /// cancelling a stop whose audio is already in flight would not un-send it. Claims the
    /// transition on success so draining the recorder cannot race a new recording.
    /// </summary>
    public bool RequestCancel()
    {
        if (!IsRecording)
        {
            return false;
        }

        IsTransitioning = true;
        return true;
    }

    public void SetRecording()
    {
        State = DictationState.Recording;
        StatusMessage = null;
    }

    public void SetProcessing()
    {
        State = DictationState.Processing;
        StatusMessage = null;
    }

    public void SetError(string message)
    {
        State = DictationState.Error;
        StatusMessage = message;
    }

    public void Reset()
    {
        State = DictationState.Idle;
        StatusMessage = null;
    }

    /// <summary>
    /// Releases the gate claimed by <see cref="RequestToggle"/> or <see cref="RequestCancel"/>.
    /// Always call it from a finally: a leaked claim wedges the app into a state where no hotkey
    /// does anything at all until the user restarts it.
    /// </summary>
    public void EndTransition() => IsTransitioning = false;
}
