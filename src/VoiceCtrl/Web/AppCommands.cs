using System.Reflection;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Status;
using VoiceCtrl.Core.Updates;

namespace VoiceCtrl.Web;

/// <summary>The <c>app.*</c> commands every surface's page may call (spec §4).</summary>
internal sealed class AppCommands(
    SurfaceAppearance appearance, ThemeStore themeStore, AppStatusTracker status, SettingsStore settings, Func<DateTimeOffset> clock)
{
    /// <summary>The product version, without the build metadata the SDK appends.</summary>
    public static string Version { get; } =
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];

    public void Register(BridgeDispatcher dispatcher)
    {
        dispatcher.Handle<AppReady, AppSnapshot>(_ =>
            Task.FromResult(new AppSnapshot(appearance.Theme, appearance.Preference, appearance.ReducedMotion, Version, status.Current,
                UpdateCard.Visible(settings.Current.App, clock()))));

        // Saving raises AppChanged, and SurfaceAppearance then tells every open surface (this one too).
        dispatcher.Handle<SetTheme>(args =>
        {
            if (!Enum.IsDefined(args.Theme))
            {
                throw new BridgeCommandException("bad_args", "Unknown Theme.");
            }

            themeStore.SetAndSave(args.Theme);
            return Task.CompletedTask;
        });

        // The tracker raises Changed, and every open surface then gets app.status.
        dispatcher.Handle<SetPaused>(args =>
        {
            status.SetPaused(args.Paused);
            return Task.CompletedTask;
        });

        dispatcher.Handle<DismissUpdateCard>(_ =>
        {
            settings.Update(s => s with { App = UpdateCard.Dismiss(s.App) });
            return Task.CompletedTask;
        });
    }
}
