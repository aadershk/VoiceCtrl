using System.Text.RegularExpressions;

namespace VoiceCtrl.Core.Diagnostics;

/// <summary>
/// Makes text safe to paste into a public GitHub issue (spec §5.3): Copy diagnostics runs every
/// line of its report through this. It drops the saved keys and anything shaped like a key,
/// quoted text (where a Dictation would appear, though the logger never writes one), email
/// addresses, and the user's folder and name.
/// <para>
/// Deliberately greedy: a scrubbed GUID or a lost quoted file name costs a little context in a
/// bug report, while a leaked key or Dictation can't be taken back.
/// </para>
/// </summary>
public sealed partial class LogScrubber
{
    public const string KeyPlaceholder = "[key]";
    public const string ProfilePlaceholder = "%USERPROFILE%";
    public const string UserNamePlaceholder = "%USERNAME%";
    public const string EmailPlaceholder = "[email]";

    // Shorter "secrets" would be too common as ordinary words to replace safely.
    private const int MinSecretLength = 8;
    private const int MinUserNameLength = 3;

    private readonly string[] _secrets;
    private readonly Regex? _profilePath;
    private readonly Regex? _userName;

    /// <param name="userProfile">The user's profile folder, e.g. C:\Users\name.</param>
    /// <param name="userName">The Windows account name.</param>
    /// <param name="secrets">The saved keys, removed wherever they appear. Nulls are skipped.</param>
    public LogScrubber(string? userProfile, string? userName, IEnumerable<string?> secrets)
    {
        // Longest first, so a key that contains another is removed whole.
        _secrets = secrets
            .Where(s => s is { Length: >= MinSecretLength })
            .Select(s => s!)
            .OrderByDescending(s => s.Length)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            _profilePath = new Regex(PathPattern(userProfile.TrimEnd('\\', '/')), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (userName is { Length: >= MinUserNameLength })
        {
            _userName = new Regex($@"\b{Regex.Escape(userName)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }

    /// <summary>For log lines: everything, including quoted text.</summary>
    public string Scrub(string text)
    {
        text = ScrubValue(text);
        text = StraightQuoted().Replace(text, "\"…\"");
        text = CurlyQuoted().Replace(text, "“…”");
        return text;
    }

    /// <summary>For a setting's value, such as a microphone's name: keys, paths, emails and the
    /// user's name, but quotes are kept, since the value is shown as it is.</summary>
    public string ScrubValue(string text)
    {
        foreach (string secret in _secrets)
        {
            text = text.Replace(secret, KeyPlaceholder, StringComparison.Ordinal);
        }

        text = GoogleKey().Replace(text, KeyPlaceholder);
        text = BearerToken().Replace(text, "Bearer " + KeyPlaceholder);
        text = LongToken().Replace(text, KeyPlaceholder);

        if (_profilePath is not null)
        {
            text = _profilePath.Replace(text, ProfilePlaceholder);
        }

        text = AnyUsersFolder().Replace(text, ProfilePlaceholder);
        text = Email().Replace(text, EmailPlaceholder);

        if (_userName is not null)
        {
            text = _userName.Replace(text, UserNamePlaceholder);
        }

        return text;
    }

    /// <summary>The profile path as a pattern that also matches it JSON-escaped (\\) or with
    /// forward slashes, as logged exception messages and URIs sometimes carry it.</summary>
    private static string PathPattern(string path)
    {
        IEnumerable<string> parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape);
        // Not followed by more of a name, so C:\Users\Sam leaves C:\Users\Samira alone.
        return string.Join(@"(?:\\\\|\\|/)", parts) + @"(?![A-Za-z0-9_.\-])";
    }

    [GeneratedRegex(@"AIza[0-9A-Za-z_\-]{30,}")]
    private static partial Regex GoogleKey();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9_\-\.=]+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    // 32+ characters of a key alphabet with at least one digit and one letter: API keys, tokens
    // and hashes. Ordinary words, times and counts are never that long.
    [GeneratedRegex(@"(?<![A-Za-z0-9_\-])(?=[A-Za-z0-9_\-]*[0-9])(?=[A-Za-z0-9_\-]*[A-Za-z])[A-Za-z0-9_\-]{32,}(?![A-Za-z0-9_\-])")]
    private static partial Regex LongToken();

    // X:\Users\<name>, with any of the three separators, for an account other than the current one.
    [GeneratedRegex(@"[A-Za-z]:(?:\\\\|\\|/)Users(?:\\\\|\\|/)[^\\/\s""'()<>,;]+", RegexOptions.IgnoreCase)]
    private static partial Regex AnyUsersFolder();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex("\"[^\"\\r\\n]*\"")]
    private static partial Regex StraightQuoted();

    [GeneratedRegex("“[^”\\r\\n]*”")]
    private static partial Regex CurlyQuoted();
}
