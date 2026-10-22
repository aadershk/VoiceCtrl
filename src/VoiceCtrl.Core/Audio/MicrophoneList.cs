namespace VoiceCtrl.Core.Audio;

/// <param name="Id">Core Audio's endpoint ID, stable across restarts and replugs.</param>
/// <param name="Name">What Windows' Sound settings call it, e.g. "Headset (Jabra Evolve2 65)".</param>
public sealed record MicrophoneDevice(string Id, string Name);

/// <summary>
/// The active capture devices at one moment, and the one Windows uses by default. Immutable, so
/// the catalog can swap in a new one while the recorder and the Settings page read the old.
/// </summary>
/// <param name="Default">Null only when there are no microphones at all.</param>
public sealed record MicrophoneList(IReadOnlyList<MicrophoneDevice> Devices, MicrophoneDevice? Default)
{
    public static readonly MicrophoneList Empty = new([], null);

    public bool HasMicrophone => Devices.Count > 0;

    /// <summary>
    /// The device a Dictation records from: the chosen one while it is connected, otherwise null,
    /// which means the Windows default (spec §5.3). The choice itself stays in settings.
    /// </summary>
    public string? RecordingDevice(string? chosenId) =>
        chosenId is not null && Devices.Any(device => device.Id == chosenId) ? chosenId : null;

    /// <summary>Record equality compares the device list by reference; this compares contents.</summary>
    public bool SameAs(MicrophoneList other) => Default == other.Default && Devices.SequenceEqual(other.Devices);
}
