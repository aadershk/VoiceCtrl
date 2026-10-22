namespace VoiceCtrl.Core.Status;

/// <summary>What the status card says (DESIGN §8). Problems outrank Paused, so the card always
/// names the thing that needs fixing; the page owns the words.</summary>
public enum AppStatusKind
{
    Ready,
    Paused,
    NoMicrophone,
    ApiKeyNotWorking,
}

/// <summary>
/// The app-wide status: the Hub's status card now, the tray flyout's header and tooltip later.
/// </summary>
/// <param name="Hotkey">The Hotkey's name for the Ready hint ("Double-tap Ctrl …").</param>
/// <param name="TalkKeys">The hold-to-talk Trigger Key's chips ("Left Ctrl", "Left Win"), for the Ready hint;
/// null once the Trigger Key was removed.</param>
/// <param name="Paused">Pause is on, whatever <paramref name="Kind"/> shows (the flyout's
/// Pause/Resume item needs it even while a problem is shown).</param>
/// <param name="OfflineFallback">With <see cref="AppStatusKind.ApiKeyNotWorking"/>: Auto Mode
/// is using Offline meanwhile. False in Online Mode, where Dictations fail until it's fixed.</param>
/// <param name="Provider">The chosen Online provider's name ("Gemini", "NVIDIA"), for "NVIDIA key not working".</param>
public sealed record AppStatus(AppStatusKind Kind, string Hotkey, bool Paused, bool OfflineFallback, IReadOnlyList<string>? TalkKeys = null, string Provider = "Gemini")
{
    // A list compares by reference in a record, which would make every refresh look like a change.
    public bool Equals(AppStatus? other) =>
        other is not null && Kind == other.Kind && Hotkey == other.Hotkey && Paused == other.Paused
        && OfflineFallback == other.OfflineFallback && Provider == other.Provider
        && (TalkKeys is null ? other.TalkKeys is null : other.TalkKeys is not null && TalkKeys.SequenceEqual(other.TalkKeys));

    public override int GetHashCode() => HashCode.Combine(Kind, Hotkey, Paused, OfflineFallback, TalkKeys?.Count);
}

/// <summary>Whether any microphone is connected, and when that changes.</summary>
public interface IMicrophonePresence
{
    bool HasMicrophone { get; }

    /// <summary>Raised on any thread after a device was added, removed, enabled or disabled.</summary>
    event Action? Changed;
}
