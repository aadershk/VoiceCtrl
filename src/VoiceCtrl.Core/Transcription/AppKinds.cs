using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Transcription;

/// <summary>The kind of app a Dictation lands in (CONTEXT.md, App kind). Each kind has its own
/// built-in layout and, but for AI prompts and code, a Style of its own.</summary>
public enum AppKind
{
    Other,
    Email,
    WorkChat,
    PersonalChat,
    Document,
    AiPrompt,
}

/// <summary>An app VoiceCtrl knows by name (ticket 56): the processes and browser sites it goes by,
/// and its kind. The Style screen lists these on their kind's card.</summary>
public sealed record KnownApp(string Name, AppKind Kind, IReadOnlyList<string> Processes, IReadOnlyList<string> Sites);

/// <summary>
/// Which <see cref="AppKind"/> an app is (tickets 55, 56). A desktop app is known by its process; a
/// browser by the site in its window title, read on this PC and never sent anywhere. An app the user
/// moved on the Style screen wins over its built-in kind.
/// </summary>
public static partial class AppKinds
{
    public static bool IsChat(this AppKind kind) => kind is AppKind.WorkChat or AppKind.PersonalChat;

    /// <summary>Every app VoiceCtrl sorts by itself. "claude" is both the Claude app and Claude Code;
    /// a console app has no window of its own, so the terminals hosting it are listed too.</summary>
    public static IReadOnlyList<KnownApp> Known { get; } =
    [
        new("Outlook", AppKind.Email, ["outlook", "olk", "hxoutlook"], ["Outlook"]),
        new("Gmail", AppKind.Email, [], ["Gmail"]),
        new("Thunderbird", AppKind.Email, ["thunderbird"], []),
        new("Mailspring", AppKind.Email, ["mailspring"], []),
        new("eM Client", AppKind.Email, ["emclient", "mailclient"], []),
        new("Mailbird", AppKind.Email, ["mailbird"], []),

        new("Slack", AppKind.WorkChat, ["slack"], ["Slack"]),
        new("Microsoft Teams", AppKind.WorkChat, ["teams", "ms-teams", "msteams"], ["Microsoft Teams"]),
        new("Google Chat", AppKind.WorkChat, [], ["Google Chat"]),
        new("Element", AppKind.WorkChat, ["element"], []),

        new("WhatsApp", AppKind.PersonalChat, ["whatsapp", "whatsapp.root"], ["WhatsApp"]),
        new("Telegram", AppKind.PersonalChat, ["telegram"], ["Telegram", "Telegram Web"]),
        new("Signal", AppKind.PersonalChat, ["signal"], []),
        new("Messenger", AppKind.PersonalChat, ["messenger"], ["Messenger"]),
        new("Discord", AppKind.PersonalChat, ["discord"], ["Discord"]),
        new("Skype", AppKind.PersonalChat, ["skype"], []),
        new("Viber", AppKind.PersonalChat, ["viber"], []),

        new("Word", AppKind.Document, ["winword"], []),
        new("Google Docs", AppKind.Document, [], ["Google Docs"]),
        new("Notion", AppKind.Document, ["notion"], []),
        new("OneNote", AppKind.Document, ["onenote"], []),
        new("Obsidian", AppKind.Document, ["obsidian"], []),
        new("WordPad", AppKind.Document, ["wordpad"], []),
        new("LibreOffice", AppKind.Document, ["soffice", "soffice.bin"], []),
        new("Scrivener", AppKind.Document, ["scrivener"], []),

        new("ChatGPT", AppKind.AiPrompt, ["chatgpt"], ["ChatGPT"]),
        new("Claude", AppKind.AiPrompt, ["claude"], ["Claude"]),
        new("Gemini", AppKind.AiPrompt, [], ["Gemini", "Google Gemini"]),
        new("Cursor", AppKind.AiPrompt, ["cursor"], []),
        new("VS Code", AppKind.AiPrompt, ["code", "code - insiders"], []),
        new("Windsurf", AppKind.AiPrompt, ["windsurf"], []),
        new("Zed", AppKind.AiPrompt, ["zed"], []),
        new("Visual Studio", AppKind.AiPrompt, ["devenv"], []),
        new("JetBrains IDEs", AppKind.AiPrompt, ["rider64", "idea64", "pycharm64"], []),
        new("Terminals", AppKind.AiPrompt, ["windowsterminal", "powershell", "pwsh", "cmd", "conhost", "mintty"], []),
    ];

    private static readonly Dictionary<string, KnownApp> ByProcess = Known
        .SelectMany(app => app.Processes.Select(process => (process, app)))
        .ToDictionary(pair => pair.process, pair => pair.app, StringComparer.OrdinalIgnoreCase);

    /// <summary>Sites by the name they put in their page title, which is the title's last part.</summary>
    private static readonly Dictionary<string, KnownApp> BySite = Known
        .SelectMany(app => app.Sites.Select(site => (site, app)))
        .ToDictionary(pair => pair.site, pair => pair.app, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "zen", "librewolf", "waterfox", "chromium",
    };

    /// <param name="windowTitle">The foreground window's title; only read for a browser.</param>
    /// <param name="extraAiPromptApps">The advanced settings' prompt style apps, which win over the built-in kind.</param>
    /// <param name="extraChatApps">The advanced settings' chat style apps (v3's), likewise; they count as work chat.</param>
    /// <param name="moved">The apps the user moved on the Style screen, which win over everything else.</param>
    public static AppKind Of(
        string? processName,
        string? windowTitle = null,
        IReadOnlyCollection<string>? extraAiPromptApps = null,
        IReadOnlyCollection<string>? extraChatApps = null,
        IReadOnlyDictionary<string, AppKind>? moved = null)
    {
        if (processName is null)
        {
            return AppKind.Other;
        }

        KnownApp? app = AppOf(processName, windowTitle);
        if (moved is not null && MovedKind(moved, app?.Name ?? processName) is { } chosen)
        {
            return chosen;
        }

        if (extraAiPromptApps is not null && extraAiPromptApps.Contains(processName, StringComparer.OrdinalIgnoreCase))
        {
            return AppKind.AiPrompt;
        }

        if (extraChatApps is not null && extraChatApps.Contains(processName, StringComparer.OrdinalIgnoreCase))
        {
            return AppKind.WorkChat;
        }

        return app?.Kind ?? AppKind.Other;
    }

    /// <summary>The known app a process (or, for a browser, the site in its title) is, if any.</summary>
    public static KnownApp? AppOf(string processName, string? windowTitle = null) =>
        ByProcess.TryGetValue(processName, out KnownApp? app) ? app
        : Browsers.Contains(processName) && windowTitle is not null ? SiteOf(windowTitle)
        : null;

    /// <summary>The known app called <paramref name="name"/> on the Style screen, if any.</summary>
    public static KnownApp? Named(string name) =>
        Known.FirstOrDefault(app => string.Equals(app.Name, name, StringComparison.OrdinalIgnoreCase));

    private static AppKind? MovedKind(IReadOnlyDictionary<string, AppKind> moved, string key)
    {
        foreach ((string name, AppKind kind) in moved)
        {
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return null;
    }

    /// <summary>
    /// Sites put their own name last ("Inbox - alex@gmail.com - Gmail", "Chat | Sarah | Microsoft
    /// Teams") and the browser adds its name after that. So the site is the last part once the
    /// browser's is gone; a subject line or page title that only mentions a site comes earlier and
    /// never counts. WhatsApp and Messenger put their name first ("(2) WhatsApp", "Messenger |
    /// Facebook"), so a title's first part counts for them alone.
    /// </summary>
    private static KnownApp? SiteOf(string windowTitle)
    {
        List<string> parts = TitleSeparator().Split(windowTitle)
            .Select(part => MoreTabsSuffix().Replace(UnreadCount().Replace(part, string.Empty), string.Empty).Trim())
            .Where(part => part.Length > 0)
            .ToList();

        // The browser's own name ("Google Chrome", "Microsoft Edge").
        if (parts.Count < 2)
        {
            return null;
        }

        parts.RemoveAt(parts.Count - 1);
        if (BySite.TryGetValue(parts[^1], out KnownApp? app))
        {
            return app;
        }

        // Edge with several profiles puts the profile's name before its own ("- Personal - Microsoft Edge").
        if (windowTitle.EndsWith("Edge", StringComparison.Ordinal) && parts.Count > 1 && BySite.TryGetValue(parts[^2], out app))
        {
            return app;
        }

        return parts[0] is "WhatsApp" or "Messenger" ? BySite[parts[0]] : null;
    }

    [GeneratedRegex(@"\s+[-|—–]\s+")]
    private static partial Regex TitleSeparator();

    /// <summary>"(3) " before a site's name: its unread count.</summary>
    [GeneratedRegex(@"^\(\d+\+?\)\s*")]
    private static partial Regex UnreadCount();

    /// <summary>Edge's " and 2 more pages" after the tab's title.</summary>
    [GeneratedRegex(@"\s+and \d+ more pages?$")]
    private static partial Regex MoreTabsSuffix();
}
