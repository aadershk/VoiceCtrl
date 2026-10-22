using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace VoiceCtrl.Core.Audio;

/// <summary>
/// <see cref="IAudioSessionMixer"/> over Windows Core Audio: the per-app volumes the Volume mixer
/// shows, on every active playback device. Each call opens its own enumerator, so it can run on
/// any thread-pool thread and holds no COM objects between calls.
/// </summary>
public sealed class CoreAudioSessionMixer : IAudioSessionMixer
{
    public IReadOnlyList<AudioSessionLevel> Sessions()
    {
        var levels = new List<AudioSessionLevel>();
        ForEachSession(session => levels.Add(new AudioSessionLevel(
            session.GetSessionInstanceIdentifier,
            (int)session.GetProcessID,
            session.SimpleAudioVolume.Volume)));
        return levels;
    }

    public void SetVolumes(IReadOnlyDictionary<string, float> volumes) =>
        ForEachSession(session =>
        {
            if (volumes.TryGetValue(session.GetSessionInstanceIdentifier, out float volume))
            {
                session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
            }
        });

    private static void ForEachSession(Action<AudioSessionControl> visit)
    {
        using var enumerator = new MMDeviceEnumerator();
        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                AudioSessionManager manager = device.AudioSessionManager;
                manager.RefreshSessions();
                SessionCollection sessions = manager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    using AudioSessionControl session = sessions[i];

                    // An expired session belongs to an app that has closed; its volume means nothing.
                    if (session.State != AudioSessionState.AudioSessionStateExpired)
                    {
                        visit(session);
                    }
                }
            }
        }
    }
}
