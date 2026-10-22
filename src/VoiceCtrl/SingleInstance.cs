using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using VoiceCtrl.Core.Config;

namespace VoiceCtrl;

/// <summary>
/// One VoiceCtrl per user session (spec §2, v3's rule ported). A second launch of the same build
/// asks the running copy to open the Hub and exits. Any other build (a newer version, or the same
/// version from another folder) asks the running copy to quit and takes over, so launching an
/// update never silently brings the old one forward. The names are v3's, so a v4 start replaces a
/// running v3.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    // Session-local (no "Global\" prefix): VoiceCtrl runs per user, and no elevation is needed.
    private const string BaseMutexName = "VoiceCtrl.SingleInstanceMutex";
    private const string BaseActivateEventName = "VoiceCtrl.ActivateRequestedEvent";
    private const string BaseQuitEventName = "VoiceCtrl.QuitRequestedEvent";
    private const string BaseBuildMarkerPrefix = "VoiceCtrl.RunningBuild.";

    private static readonly TimeSpan TakeOverTimeout = TimeSpan.FromSeconds(5);

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private readonly EventWaitHandle _quitEvent;
    private readonly EventWaitHandle _buildMarker;
    private readonly RegisteredWaitHandle _activateWait;
    private readonly RegisteredWaitHandle _quitWait;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Name(BaseActivateEventName));
        _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Name(BaseQuitEventName));
        _buildMarker = new EventWaitHandle(false, EventResetMode.ManualReset, BuildMarkerName());

        // Raised on the thread pool; the host marshals them to the UI thread.
        _activateWait = ThreadPool.RegisterWaitForSingleObject(_activateEvent, (_, _) => ActivateRequested?.Invoke(), null, Timeout.Infinite, false);
        _quitWait = ThreadPool.RegisterWaitForSingleObject(_quitEvent, (_, _) => QuitRequested?.Invoke(), null, Timeout.Infinite, false);
    }

    /// <summary>Another launch of this build wants the Hub. Raised on a thread-pool thread.</summary>
    public event Action? ActivateRequested;

    /// <summary>Another build is taking over. Raised on a thread-pool thread.</summary>
    public event Action? QuitRequested;

    /// <summary>
    /// Becomes the running instance, or returns null when this launch should just exit (the same
    /// build is already running and has been asked to open the Hub).
    /// </summary>
    public static SingleInstance? Acquire()
    {
        var mutex = new Mutex(initiallyOwned: true, Name(BaseMutexName), out bool isFirst);
        if (isFirst)
        {
            return new SingleInstance(mutex);
        }

        if (EventWaitHandle.TryOpenExisting(BuildMarkerName(), out EventWaitHandle? sameBuild))
        {
            sameBuild.Dispose();
            Signal(BaseActivateEventName);
            mutex.Dispose();
            return null;
        }

        Signal(BaseQuitEventName);
        bool tookOver;
        try
        {
            tookOver = mutex.WaitOne(TakeOverTimeout);
        }
        catch (AbandonedMutexException)
        {
            // The old instance died without releasing it; ownership passes to us all the same.
            tookOver = true;
        }

        if (tookOver)
        {
            return new SingleInstance(mutex);
        }

        // Most likely an old build with no quit listener. Bring it forward rather than leaving
        // this launch looking like it did nothing.
        Signal(BaseActivateEventName);
        mutex.Dispose();
        return null;
    }

    private static void Signal(string baseName)
    {
        if (EventWaitHandle.TryOpenExisting(Name(baseName), out EventWaitHandle? handle))
        {
            using (handle)
            {
                handle.Set();
            }
        }
    }

    /// <summary>The sandbox harness runs beside the owner's real VoiceCtrl, never replacing it.</summary>
    private static string Name(string baseName) =>
        UserDataPaths.SandboxRoot is null ? baseName : baseName + ".Sandbox";

    /// <summary>Identifies this exact build: its version and where it runs from.</summary>
    private static string BuildMarkerName()
    {
        string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        string identity = $"{version}|{Environment.ProcessPath}".ToUpperInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        return Name(BaseBuildMarkerPrefix + hash);
    }

    public void Dispose()
    {
        _activateWait.Unregister(null);
        _quitWait.Unregister(null);
        _activateEvent.Dispose();
        _quitEvent.Dispose();
        _buildMarker.Dispose();

        // Released explicitly, so a newer instance waiting on it wakes with a clean hand-over.
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
