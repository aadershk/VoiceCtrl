using System.IO;

namespace VoiceCtrl.Core.Config;

/// <summary>
/// The one place that knows where VoiceCtrl keeps per-user state. Everything lives under
/// LocalApplicationData rather than next to the exe so it stays writable wherever the app is
/// installed and survives replacing the exe folder wholesale, which is how a release upgrade
/// happens here.
/// </summary>
public static class UserDataPaths
{
    /// <summary>
    /// The agent's sandbox harness (spec §10): a Debug build started with <c>VOICECTRL_DATA_ROOT</c>
    /// keeps all its data there, so it never reads or writes the owner's real Dictations, settings
    /// or key. Always null in a Release build, which ignores the variable.
    /// </summary>
    // Must stay above Root: static initializers run in textual order, and Root reads this one.
    public static string? SandboxRoot { get; } = ReadSandboxRoot();

    public static string Root { get; } = SandboxRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceCtrl");

    private static string? ReadSandboxRoot()
    {
#if DEBUG
        return Environment.GetEnvironmentVariable("VOICECTRL_DATA_ROOT") is { Length: > 0 } root ? Path.GetFullPath(root) : null;
#else
        return null;
#endif
    }

    /// <summary>The WebView2 user data folder (cache, local storage), shared by every surface.
    /// Under the data root, never the install folder, which each update replaces.</summary>
    public static string WebView2 => Path.Combine(Root, "webview2");

    internal const string SettingsFileName = "settings.json";
    internal const string SecretsFileName = "secrets.bin";

    public static string Log => Path.Combine(Root, "log.txt");
    public static string Models => Path.Combine(Root, "models");

    /// <summary>Exists only while VoiceCtrl is starting; see <see cref="Updates.StartupMarker"/>.</summary>
    public static string StartupMarker => Path.Combine(Root, "starting.json");

    /// <summary>Every user-facing setting; see <see cref="SettingsStore"/>. v3's prefs.json,
    /// theme.json and hotkey.json are only read once, by <see cref="V3Migration"/>.</summary>
    public static string Settings => Path.Combine(Root, SettingsFileName);

    /// <summary>The Gemini and TypeSafe keys, DPAPI-encrypted; see <see cref="SecretsStore"/>.</summary>
    public static string Secrets => Path.Combine(Root, SecretsFileName);

    /// <summary>Proper nouns and jargon the speaker uses, one per line.</summary>
    public static string Dictionary => Path.Combine(Root, "dictionary.txt");

    /// <summary>Spoken shorthand to expand after transcription, "trigger = expansion" per line.</summary>
    public static string Snippets => Path.Combine(Root, "snippets.txt");

    /// <summary>Per-application tone/formatting overrides, keyed by process name.</summary>
    public static string Profiles => Path.Combine(Root, "profiles.json");

    /// <summary>The last ~50 Dictations (text and timestamp only, never audio) backing the Hub's
    /// Dictations screen.</summary>
    public static string History => Path.Combine(Root, "history.json");

    /// <summary>The engine record (VED-77): what the voice engine did per Dictation, no text. Exists
    /// only while its Settings switch is on.</summary>
    public static string EngineRecord => Path.Combine(Root, "engine-record.json");

    /// <summary>Dictionary Suggestions the user ignored, one per line, so they never come back.</summary>
    public static string IgnoredSuggestions => Path.Combine(Root, "suggestions-ignored.txt");

    /// <summary>
    /// Creates a user-editable file with starter content if it does not exist yet, and returns the
    /// path either way. Never overwrites: the file is the user's, and losing their dictionary to a
    /// version upgrade would be worse than shipping without the seed content.
    /// </summary>
    public static string EnsureSeeded(string path, string seedContents)
    {
        try
        {
            Directory.CreateDirectory(Root);
            if (!File.Exists(path))
            {
                File.WriteAllText(path, seedContents);
            }
        }
        catch (IOException)
        {
            // Seeding is a convenience. The loaders below all treat a missing file as "no entries",
            // so a failure here costs discoverability, never a working app.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return path;
    }
}
