using VoiceCtrl.Core.Config;

namespace VoiceCtrl.Core.Updates;

/// <summary>The "Updated to" card's saved state: which version was installed, and when this app
/// first started on it.</summary>
public sealed record UpdateCardState(string Version, DateTimeOffset InstalledAt);

/// <summary>
/// When the Hub sidebar shows "Updated to {version}" (DESIGN §12, spec §9). The card appears
/// after an update was applied and leaves after ✕, See what's new, or 7 days; a newer update
/// replaces it. Only a start of a newer version than the last one run counts, so a fresh install
/// and a v3 upgrade (neither has a <see cref="AppSection.LastRunVersion"/>) show nothing.
/// </summary>
public static class UpdateCard
{
    public static readonly TimeSpan ShownFor = TimeSpan.FromDays(7);

    /// <summary>The bookkeeping for one start of <paramref name="currentVersion"/>.</summary>
    public static AppSection AtStart(AppSection app, string currentVersion, DateTimeOffset now)
    {
        // A version newer than one the user went back from is the fix they were waiting for.
        string? skipped = app.SkippedUpdateVersion is { } s && AppVersion.Compare(currentVersion, s) > 0 ? null : app.SkippedUpdateVersion;

        if (app.LastRunVersion is not { } last)
        {
            return app with { LastRunVersion = currentVersion, SkippedUpdateVersion = skipped };
        }

        int order = AppVersion.Compare(currentVersion, last);
        UpdateCardState? card = order > 0 ? new UpdateCardState(currentVersion, now)
            : order < 0 ? null
            : app.UpdateCard;
        return app with { LastRunVersion = currentVersion, UpdateCard = card, SkippedUpdateVersion = skipped };
    }

    /// <summary>The version the card announces, or null when there is no card to show.</summary>
    public static string? Visible(AppSection app, DateTimeOffset now) =>
        app.UpdateCard is { } card && now < card.InstalledAt + ShownFor ? card.Version : null;

    /// <summary>✕ or See what's new.</summary>
    public static AppSection Dismiss(AppSection app) => app with { UpdateCard = null };
}

/// <summary>Compares versions by SemVer precedence, as Velopack does when it decides whether a
/// release is an update: major.minor.patch by number, a pre-release (<c>4.0.0-rc.2</c>) older than
/// its release, pre-release parts by number where numeric. Build metadata (<c>+sha</c>) is ignored.
/// The two-PC trial runs <c>4.0.0-rc.N</c> builds that must see 4.0.0 as newer.</summary>
public static class AppVersion
{
    public static int Compare(string a, string b)
    {
        (Version coreA, string[] preA) = Parse(a);
        (Version coreB, string[] preB) = Parse(b);

        int core = coreA.CompareTo(coreB);
        if (core != 0)
        {
            return core;
        }

        // No pre-release outranks any pre-release of the same core version.
        if (preA.Length == 0 || preB.Length == 0)
        {
            return preB.Length.CompareTo(preA.Length);
        }

        for (int i = 0; i < Math.Min(preA.Length, preB.Length); i++)
        {
            int part = ComparePart(preA[i], preB[i]);
            if (part != 0)
            {
                return part;
            }
        }

        return preA.Length.CompareTo(preB.Length);
    }

    private static int ComparePart(string a, string b)
    {
        bool numericA = long.TryParse(a, out long numberA);
        bool numericB = long.TryParse(b, out long numberB);
        if (numericA && numericB)
        {
            return numberA.CompareTo(numberB);
        }

        // SemVer: numeric identifiers rank below alphanumeric ones.
        return numericA ? -1 : numericB ? 1 : string.CompareOrdinal(a, b);
    }

    private static (Version Core, string[] PreRelease) Parse(string text)
    {
        string withoutBuild = text.Split('+')[0];
        int dash = withoutBuild.IndexOf('-');
        string core = dash < 0 ? withoutBuild : withoutBuild[..dash];
        string[] pre = dash < 0 ? [] : withoutBuild[(dash + 1)..].Split('.');
        return (Version.TryParse(core, out Version? version) ? version : new Version(0, 0), pre);
    }
}
