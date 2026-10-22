namespace VoiceCtrl.Core.Transcription;

/// <summary>How a Transcription reads in one kind of app (CONTEXT.md, Style; ticket 56): its
/// capitals, closing punctuation and register. The app kind still decides the layout.</summary>
public enum Style
{
    Formal,
    Casual,
    VeryCasual,
    Excited,
}

/// <summary>The Style each kind of app gets, the user's name for sign-offs, and the apps the user
/// moved to another kind. Kept in settings.json; a fresh install gets the defaults below.</summary>
public sealed record StyleSettings
{
    public Style PersonalChat { get; init; } = Style.Casual;

    public Style WorkChat { get; init; } = Style.Casual;

    public Style Email { get; init; } = Style.Formal;

    public Style Document { get; init; } = Style.Formal;

    public Style Other { get; init; } = Style.Formal;

    /// <summary>Used only to sign an email VoiceCtrl writes from a spoken request (ticket 57).
    /// Empty: the sign-off has no name.</summary>
    public string YourName { get; init; } = string.Empty;

    /// <summary>An app the user moved to another kind, by its name on the Style screen ("Discord")
    /// or, for an app VoiceCtrl doesn't list, its process name.</summary>
    public IReadOnlyDictionary<string, AppKind> AppKinds { get; init; } = new Dictionary<string, AppKind>();

    /// <summary>The Style for <paramref name="kind"/>; null for AI prompts and code, whose words
    /// and capitals are kept as said.</summary>
    public Style? For(AppKind kind) => kind switch
    {
        AppKind.PersonalChat => PersonalChat,
        AppKind.WorkChat => WorkChat,
        AppKind.Email => Email,
        AppKind.Document => Document,
        AppKind.Other => Other,
        _ => null,
    };

    public StyleSettings With(AppKind kind, Style style) => kind switch
    {
        AppKind.PersonalChat => this with { PersonalChat = style },
        AppKind.WorkChat => this with { WorkChat = style },
        AppKind.Email => this with { Email = style },
        AppKind.Document => this with { Document = style },
        AppKind.Other => this with { Other = style },
        _ => this,
    };
}

/// <summary>A Style as a Profile stores it in profiles.json: "formal", "casual", "veryCasual",
/// "excited". Read leniently, since the file can be edited by hand.</summary>
public static class StyleNames
{
    public static Style? Parse(string? stored) => stored?.Replace(" ", string.Empty).Trim().ToLowerInvariant() switch
    {
        "formal" => Style.Formal,
        "casual" => Style.Casual,
        "verycasual" => Style.VeryCasual,
        "excited" => Style.Excited,
        _ => null,
    };

    public static string Stored(Style style) => style switch
    {
        Style.Casual => "casual",
        Style.VeryCasual => "veryCasual",
        Style.Excited => "excited",
        _ => "formal",
    };
}
