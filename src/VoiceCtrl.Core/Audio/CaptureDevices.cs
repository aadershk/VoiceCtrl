using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace VoiceCtrl.Core.Audio;

/// <summary>Opens the microphone a Dictation (or the Settings level preview) records from.</summary>
internal static class CaptureDevices
{
    /// <summary>
    /// The device with <paramref name="deviceId"/> while it is connected, otherwise the Windows
    /// default communications device (spec §5.3: an unplugged choice falls back to the default).
    /// Throws <see cref="COMException"/> when there is no microphone at all.
    /// </summary>
    public static MMDevice Open(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (deviceId is not null)
        {
            try
            {
                MMDevice chosen = enumerator.GetDevice(deviceId);
                if (chosen.State == DeviceState.Active)
                {
                    return chosen;
                }

                chosen.Dispose();
            }
            catch (COMException)
            {
                // Removed since the list was read; the default will do.
            }
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
    }
}
