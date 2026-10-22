using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// The Style screen's side of the settings (ticket 56): one group per kind of app with its Style,
/// its apps and a live example in every Style, the user's name, and moving an app to another kind.
/// The examples go through <see cref="PasteLayout"/> exactly as a Dictation would, so the screen
/// shows what the engine really does.
/// </summary>
/// <param name="isOrdinaryWord">For Very casual's examples: whether a lower-case word is ordinary English.</param>
public sealed class StyleSettingsService(SettingsStore settings, Func<string, bool> isOrdinaryWord)
{
    public const int MaxNameLength = 60;

    /// <summary>The groups in the screen's order. AI prompts and code come last and have no Style.</summary>
    public static IReadOnlyList<AppKind> Groups { get; } =
        [AppKind.PersonalChat, AppKind.WorkChat, AppKind.Email, AppKind.Document, AppKind.Other, AppKind.AiPrompt];

    /// <summary>What a Dictation in each kind might come back as before its Style, punctuated as
    /// either engine would. Chosen so every Style shows a visible change.</summary>
    private static readonly Dictionary<AppKind, string> ExampleWords = new()
    {
        [AppKind.PersonalChat] = "Yeah, sounds good. I'll be there in five minutes.",
        [AppKind.WorkChat] = "I've pushed the fix. Can you take a look when you're free?",
        [AppKind.Email] = "Hi Sam, thanks for sending this over. I'll read it tonight and get back to you tomorrow. Best, Alex",
        [AppKind.Document] = "We're launching in March. It's a tight timeline, but we're ready.",
        [AppKind.Other] = "Yeah, I'll send the file tonight. Let me know if it doesn't open.",
        [AppKind.AiPrompt] = "Don't rename the API client. Just fix the retry logic.",
    };

    public StyleView View()
    {
        StyleSettings styles = settings.Current.Style;
        return new StyleView(
            Groups.Select(kind => new StyleGroup(kind, styles.For(kind), AppsIn(kind, styles.AppKinds), Examples(kind))).ToList(),
            styles.YourName);
    }

    public StyleView SetStyle(AppKind kind, Style style)
    {
        if (kind != AppKind.AiPrompt)
        {
            settings.Update(current => current with { Style = current.Style.With(kind, style) });
        }

        return View();
    }

    public StyleView SetName(string name)
    {
        string trimmed = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        string kept = trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength].TrimEnd() : trimmed;
        settings.Update(current => current with { Style = current.Style with { YourName = kept } });
        return View();
    }

    /// <summary>Moves an app to <paramref name="kind"/>. <paramref name="app"/> is a listed app's
    /// name or an unlisted app's process name. Moving an app back to where it belongs by itself
    /// forgets the move, so the file only holds real changes.</summary>
    public StyleView MoveApp(string app, AppKind kind)
    {
        string key = app.Trim();
        if (key.Length == 0)
        {
            return View();
        }

        KnownApp? known = AppKinds.Named(key);
        string stored = known?.Name ?? key;
        AppKind own = known?.Kind ?? AppKind.Other;

        settings.Update(current =>
        {
            var moved = new Dictionary<string, AppKind>(current.Style.AppKinds.Where(pair =>
                !string.Equals(pair.Key, stored, StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
            if (kind != own)
            {
                moved[stored] = kind;
            }

            return current with { Style = current.Style with { AppKinds = moved } };
        });
        return View();
    }

    private static List<StyleApp> AppsIn(AppKind kind, IReadOnlyDictionary<string, AppKind> moved)
    {
        var apps = new List<StyleApp>();
        foreach (KnownApp app in AppKinds.Known)
        {
            AppKind now = MovedKind(moved, app.Name) ?? app.Kind;
            if (now == kind)
            {
                apps.Add(new StyleApp(app.Name, app.Name, Moved: now != app.Kind));
            }
        }

        // Apps VoiceCtrl doesn't list, which the user added to a kind by their process name.
        foreach ((string name, AppKind appKind) in moved)
        {
            if (appKind == kind && AppKinds.Named(name) is null)
            {
                apps.Add(new StyleApp(name, DisplayName(name), Moved: true));
            }
        }

        return apps;
    }

    private static AppKind? MovedKind(IReadOnlyDictionary<string, AppKind> moved, string name) =>
        moved.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) is { Key: not null } found
            ? found.Value
            : null;

    private StyleExample[] Examples(AppKind kind)
    {
        string words = ExampleWords[kind];
        if (kind == AppKind.AiPrompt)
        {
            return [new StyleExample(null, Finish(words, kind, null))];
        }

        return [.. Enum.GetValues<Style>().Select(style => new StyleExample(style, Finish(words, kind, style)))];
    }

    private string Finish(string words, AppKind kind, Style? style)
    {
        string? formatting = FormattingHintMapper.For(kind);
        var context = new PromptContext(
            CleanupLevels.Standard, null, formatting, null,
            EmailLayout: kind == AppKind.Email,
            Chat: FormattingHintMapper.ChatLayoutOf(formatting),
            Kind: kind,
            Style: style);
        return PasteLayout.Finish(words, context, isOrdinaryWord);
    }

    /// <summary>"mattermost" reads as "Mattermost".</summary>
    private static string DisplayName(string processName) =>
        processName.Length == 0 ? processName : char.ToUpperInvariant(processName[0]) + processName[1..];
}
