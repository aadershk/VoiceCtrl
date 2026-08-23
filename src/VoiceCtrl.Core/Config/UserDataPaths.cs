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
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceCtrl");

    public static string Log => Path.Combine(Root, "log.txt");
    public static string Prefs => Path.Combine(Root, "prefs.json");
    public static string Models => Path.Combine(Root, "models");

    /// <summary>Proper nouns and jargon the speaker uses, one per line.</summary>
    public static string Dictionary => Path.Combine(Root, "dictionary.txt");

    /// <summary>Spoken shorthand to expand after transcription, "trigger = expansion" per line.</summary>
    public static string Snippets => Path.Combine(Root, "snippets.txt");

    /// <summary>Per-application tone/formatting overrides, keyed by process name.</summary>
    public static string Profiles => Path.Combine(Root, "profiles.json");

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
