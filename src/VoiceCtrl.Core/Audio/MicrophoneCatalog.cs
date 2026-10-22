using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Status;

namespace VoiceCtrl.Core.Audio;

/// <summary>
/// The microphones Windows has, kept current through Core Audio's endpoint notifications (spec
/// §5.3): the Settings microphone menu lists them, the recorder picks from them, and the status
/// card's "No microphone" reads <see cref="HasMicrophone"/>. The one Core Audio listener in the app.
/// </summary>
public sealed class MicrophoneCatalog : IMicrophonePresence, IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private volatile MicrophoneList _current;
    private volatile bool _unreadable;
    private int _refreshing;

    public MicrophoneCatalog()
    {
        MicrophoneList? first = Read();
        _current = first ?? MicrophoneList.Empty;
        _unreadable = first is null;
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public MicrophoneList Current => _current;

    /// <summary>An unreadable device list counts as "has one": a false "No microphone" is worse.</summary>
    public bool HasMicrophone => _unreadable || _current.HasMicrophone;

    /// <summary>Raised on a thread-pool thread whenever the list or the default changes.</summary>
    public event Action? Changed;

    /// <summary>
    /// The default is the communications device, as v3 recorded from: the one Windows switches to
    /// when a headset is plugged in. A fresh enumerator per read, because reads run on the thread
    /// pool and the registered one belongs to the UI thread's apartment. Null when Core Audio can't
    /// be read at all.
    /// </summary>
    private static MicrophoneList? Read()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = new List<MicrophoneDevice>();
            foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                {
                    devices.Add(new MicrophoneDevice(device.ID, device.FriendlyName));
                }
            }

            MicrophoneDevice? fallback = null;
            if (devices.Count > 0 && enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications))
            {
                using MMDevice defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                fallback = devices.FirstOrDefault(device => device.Id == defaultDevice.ID);
            }

            return new MicrophoneList(devices, fallback ?? devices.FirstOrDefault());
        }
        catch (COMException ex)
        {
            SimpleFileLogger.LogError("Listing microphones", ex);
            return null;
        }
    }

    /// <summary>Core Audio forbids blocking calls inside its callbacks, and a plug-in raises
    /// several at once, so the re-read runs once, off the callback thread.</summary>
    private void ScheduleRefresh()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            Interlocked.Exchange(ref _refreshing, 0);

            // An unreadable list keeps the last good one.
            MicrophoneList? next = Read();
            if (next is null || (next.SameAs(_current) && !_unreadable))
            {
                return;
            }

            _current = next;
            _unreadable = false;
            Changed?.Invoke();
        });
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) => ScheduleRefresh();

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) => ScheduleRefresh();

    void IMMNotificationClient.OnDeviceRemoved(string deviceId) => ScheduleRefresh();

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Capture && role == Role.Communications)
        {
            ScheduleRefresh();
        }
    }

    // A rename in Sound settings arrives here, among many property changes that don't matter.
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        if (key.formatId == PropertyKeys.PKEY_Device_FriendlyName.formatId && key.propertyId == PropertyKeys.PKEY_Device_FriendlyName.propertyId)
        {
            ScheduleRefresh();
        }
    }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(this);
        _enumerator.Dispose();
    }
}
