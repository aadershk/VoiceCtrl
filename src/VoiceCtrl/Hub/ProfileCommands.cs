using System.Diagnostics;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Logging;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Hub;

/// <summary>
/// The Hub's <c>profiles.*</c> commands (spec §5.3). The rules live in <see cref="ProfileLibrary"/>;
/// this adds what only the host can do, listing the apps that have a window open.
/// </summary>
/// <param name="movedApps">The apps moved to another kind on the Style screen.</param>
internal sealed class ProfileCommands(ProfileLibrary library, Func<IReadOnlyDictionary<string, AppKind>> movedApps)
{
    public void Register(BridgeDispatcher dispatcher)
    {
        dispatcher.Handle<ListProfiles, ProfileList>(_ => Task.FromResult(new ProfileList(library.Profiles())));

        // Reading each process's version info takes a moment per app, so it stays off the UI thread.
        dispatcher.Handle<ListRunningApps, RunningAppList>(_ =>
            Task.Run(() => new RunningAppList(ProfileLibrary.RunningApps(AppWindows(), library.Profiles(), movedApps()))));

        dispatcher.Handle<SaveProfile, ProfileSaveResult>(args =>
        {
            ProfileEditError error = library.Save(args.Original, args.Profile ?? throw BadArgs());
            return Task.FromResult(new ProfileSaveResult(error, library.Profiles()));
        });

        dispatcher.Handle<RemoveProfile, ProfileList>(args =>
        {
            library.Remove(args.ProcessName ?? throw BadArgs());
            return Task.FromResult(new ProfileList(library.Profiles()));
        });

        dispatcher.Handle<RestoreProfile, ProfileList>(args =>
        {
            library.Restore(args.Profile ?? throw BadArgs());
            return Task.FromResult(new ProfileList(library.Profiles()));
        });
    }

    /// <summary>Every process with a titled main window, which is what Task Manager calls an app.
    /// The description is null where Windows won't let us read it (an elevated app).</summary>
    private static List<AppWindow> AppWindows()
    {
        var windows = new List<AppWindow>();
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    {
                        continue;
                    }

                    windows.Add(new AppWindow(process.ProcessName, Description(process)));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The process exited while we looked at it.
                }
            }
        }

        SimpleFileLogger.LogInfo($"Profiles: {windows.Count} app windows open");
        return windows;
    }

    private static string? Description(Process process)
    {
        try
        {
            return process.MainModule?.FileVersionInfo.FileDescription;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static BridgeCommandException BadArgs() => new("bad_args", "Missing argument.");
}
