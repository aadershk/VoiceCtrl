namespace VoiceCtrl.Overlay;

public enum OverlayState
{
    Idle,
    Recording,
    Processing,
    Error,
}

public sealed class OverlayViewModel
{
    public OverlayState State { get; private set; } = OverlayState.Idle;
    public string? StatusMessage { get; private set; }

    public OverlayState ToggleRecording()
    {
        State = State == OverlayState.Idle ? OverlayState.Recording : OverlayState.Idle;
        StatusMessage = null;
        return State;
    }

    public void SetProcessing()
    {
        State = OverlayState.Processing;
        StatusMessage = null;
    }

    public void SetError(string message)
    {
        State = OverlayState.Error;
        StatusMessage = message;
    }

    public void Reset()
    {
        State = OverlayState.Idle;
        StatusMessage = null;
    }
}
