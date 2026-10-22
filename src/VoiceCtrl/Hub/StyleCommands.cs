using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Hub;

/// <summary>The Hub's <c>style.*</c> commands (ticket 56). The rules live in <see cref="StyleSettingsService"/>.</summary>
internal sealed class StyleCommands(StyleSettingsService styles)
{
    public void Register(BridgeDispatcher dispatcher)
    {
        // The examples use the built-in word list, which may still be loading just after start.
        dispatcher.Handle<GetStyle, StyleView>(_ => Task.Run(styles.View));

        dispatcher.Handle<SetStyle, StyleView>(args => Task.Run(() => styles.SetStyle(args.Kind, args.Style)));

        dispatcher.Handle<SetStyleName, StyleView>(args => Task.Run(() => styles.SetName(args.Name ?? throw BadArgs())));

        dispatcher.Handle<MoveStyleApp, StyleView>(args => Task.Run(() => styles.MoveApp(args.App ?? throw BadArgs(), args.Kind)));
    }

    private static BridgeCommandException BadArgs() => new("bad_args", "Missing argument.");
}
