using System.IO;
using System.Text;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Personalization;

/// <summary>
/// One Profile as the Hub shows and edits it. Every field is filled in: <see cref="Formatting"/> is
/// what the app gets now (the built-in choice when the file leaves it unset), <see cref="Cleanup"/>
/// is <see cref="ProfileLibrary.DefaultCleanup"/> or a Cleanup Level, <see cref="Style"/> is empty for
/// "Same as its kind" or a stored Style name, and <see cref="Instructions"/> is "How I write here"
/// (ticket 56). <see cref="Tone"/> carries a hand-written tone the Hub no longer shows: a Profile's
/// old Tone text is shown as its Instructions instead, and moves there when the Profile is saved.
/// <see cref="Kind"/> is the app's kind, after any move on the Style screen, for "Same as Work chat".
/// </summary>
public sealed record ProfileEntry(
    string ProcessName,
    string AppName,
    string Tone,
    string Formatting,
    string Cleanup,
    bool EmailLayout,
    string Instructions,
    string Style = "",
    AppKind Kind = AppKind.Other);

/// <summary>A window's app, as the host found it: the process name and its FileDescription, if readable.</summary>
public sealed record AppWindow(string ProcessName, string? Description);

/// <summary>An app the Profile Editor can pick. <see cref="Formatting"/> and <see cref="EmailLayout"/>
/// are what it gets today, for a new Profile's starting choices.</summary>
public sealed record RunningApp(string ProcessName, string AppName, string Formatting, bool HasProfile, bool EmailLayout, AppKind Kind = AppKind.Other);

/// <summary>Why a Profile wasn't saved. The Hub shows each under the field it names.</summary>
public enum ProfileEditError
{
    None,
    AppMissing,
    AlreadyThere,
    Full,
    ToneTooLong,
    FormattingInvalid,
    CleanupInvalid,
    InstructionsTooLong,
    StyleInvalid,
}

/// <summary>
/// The Hub's side of Profiles (spec §5.3): reads and edits <c>profiles.json</c>. Like
/// <see cref="PersonalizationLibrary"/>, every change rereads the file first, so an edit made in
/// Notepad meanwhile is kept, and writes it whole and atomically, keeping the file's
/// <c>_comment</c>. Transcription picks the change up on its next read.
/// </summary>
/// <param name="movedApps">The apps moved to another kind on the Style screen, for each entry's kind.</param>
public sealed class ProfileLibrary(string profilesPath, Func<IReadOnlyDictionary<string, AppKind>>? movedApps = null)
{
    /// <summary>The Cleanup choice that stores nothing, so the app follows Settings' Default cleanup.</summary>
    public const string DefaultCleanup = "default";

    public const int MaxToneLength = 200;

    /// <summary>"How I write here" is a short note, as Tone was.</summary>
    public const int MaxInstructionsLength = 200;

    public const int MaxProfiles = 100;

    public static IReadOnlyList<string> Formattings { get; } = [AppProfile.Structured, AppProfile.Prose, AppProfile.Paragraphs, AppProfile.None];

    // Windows' own surfaces and VoiceCtrl itself: nobody dictates into them, and a Profile for one
    // would never be used.
    private static readonly HashSet<string> NotPickable = new(StringComparer.OrdinalIgnoreCase)
    {
        "VoiceCtrl",
        "explorer",
        "ApplicationFrameHost",
        "ShellExperienceHost",
        "StartMenuExperienceHost",
        "SearchHost",
        "TextInputHost",
        "SystemSettings",
        "LockApp",
        // WebView2's own process, VoiceCtrl's included: the app that hosts it is the one to pick.
        "msedgewebview2",
    };

    public static ProfileLibrary CreateDefault() => new(UserDataPaths.Profiles);

    /// <summary>Every Profile, by app name.</summary>
    public IReadOnlyList<ProfileEntry> Profiles()
    {
        IReadOnlyDictionary<string, AppKind>? moved = movedApps?.Invoke();
        return ReadTable().Entries
            .Select(e => ToEntry(e.Key, e.Value, moved))
            .OrderBy(p => p.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Adds a Profile (<paramref name="original"/> null) or replaces the one for the app
    /// <paramref name="original"/>, which may move it to another app. An original that is gone
    /// (removed in Notepad) adds instead.</summary>
    public ProfileEditError Save(string? original, ProfileEntry profile)
    {
        ProfileEditError error = Validate(profile, out string processName, out AppProfile stored);
        if (error != ProfileEditError.None)
        {
            return error;
        }

        Dictionary<string, AppProfile> profiles = ReadProfiles();
        string? replacing = original is null ? null : profiles.Keys.FirstOrDefault(k => SameApp(k, AppProfileTable.NormalizeProcessName(original)));
        if (profiles.Keys.Any(k => SameApp(k, processName) && !SameApp(k, replacing ?? string.Empty)))
        {
            return ProfileEditError.AlreadyThere;
        }

        if (replacing is null && profiles.Count >= MaxProfiles)
        {
            return ProfileEditError.Full;
        }

        if (replacing is not null)
        {
            profiles.Remove(replacing);
        }

        profiles[processName] = stored;
        Write(profiles);
        return ProfileEditError.None;
    }

    /// <summary>Removes the app's Profile. False if it had none.</summary>
    public bool Remove(string processName)
    {
        Dictionary<string, AppProfile> profiles = ReadProfiles();
        string? key = profiles.Keys.FirstOrDefault(k => SameApp(k, AppProfileTable.NormalizeProcessName(processName)));
        if (key is null)
        {
            return false;
        }

        profiles.Remove(key);
        Write(profiles);
        return true;
    }

    /// <summary>Undo for <see cref="Remove"/>. Nothing happens if the app has a Profile again by
    /// now: the newer one is what the user made last.</summary>
    public void Restore(ProfileEntry removed) => Save(original: null, removed);

    /// <summary>The apps the Profile Editor offers: one per process, by name, leaving out Windows'
    /// own surfaces and VoiceCtrl.</summary>
    public static IReadOnlyList<RunningApp> RunningApps(
        IEnumerable<AppWindow> windows, IReadOnlyList<ProfileEntry> profiles, IReadOnlyDictionary<string, AppKind>? moved = null) =>
        windows
            .Where(w => !string.IsNullOrWhiteSpace(w.ProcessName) && !NotPickable.Contains(w.ProcessName))
            .DistinctBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(w => new RunningApp(
                w.ProcessName,
                string.IsNullOrWhiteSpace(w.Description) ? DisplayName(w.ProcessName) : w.Description.Trim(),
                FormattingHintMapper.BuiltIn(w.ProcessName),
                profiles.Any(p => SameApp(p.ProcessName, w.ProcessName)),
                BuiltInEmailLayout(w.ProcessName),
                AppKinds.Of(w.ProcessName, moved: moved)))
            .OrderBy(a => a.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static ProfileEditError Validate(ProfileEntry profile, out string processName, out AppProfile stored)
    {
        processName = AppProfileTable.NormalizeProcessName(profile.ProcessName ?? string.Empty);
        stored = new AppProfile();

        // A process name, never a path: "outlook", not "C:\...\OUTLOOK.EXE".
        if (processName.Length == 0 || processName.AsSpan().IndexOfAny(['\\', '/', ':', '"', '\r', '\n']) >= 0)
        {
            return ProfileEditError.AppMissing;
        }

        string tone = string.Join(' ', (profile.Tone ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())).Trim();
        if (tone.Length > MaxToneLength)
        {
            return ProfileEditError.ToneTooLong;
        }

        string instructions = string.Join(' ', (profile.Instructions ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())).Trim();
        if (instructions.Length > MaxInstructionsLength)
        {
            return ProfileEditError.InstructionsTooLong;
        }

        string style = (profile.Style ?? string.Empty).Trim();
        Transcription.Style? parsedStyle = StyleNames.Parse(style);
        if (style.Length > 0 && parsedStyle is null)
        {
            return ProfileEditError.StyleInvalid;
        }

        if (!Formattings.Contains(profile.Formatting, StringComparer.Ordinal))
        {
            return ProfileEditError.FormattingInvalid;
        }

        if (profile.Cleanup != DefaultCleanup && !CleanupLevels.IsValid(profile.Cleanup))
        {
            return ProfileEditError.CleanupInvalid;
        }

        string appName = (profile.AppName ?? string.Empty).Trim();
        stored = new AppProfile
        {
            App = appName.Length == 0 ? null : appName,
            Tone = tone.Length == 0 ? null : tone,
            Formatting = profile.Formatting,
            Cleanup = profile.Cleanup == DefaultCleanup ? null : profile.Cleanup,
            // What the app gets anyway is left out of the file, the same as a hand-written Profile
            // that never mentions it; only a change from that (off for an email app) is written down.
            EmailLayout = profile.EmailLayout == BuiltInEmailLayout(processName) ? null : profile.EmailLayout,
            Instructions = instructions.Length == 0 ? null : instructions,
            Style = parsedStyle is { } chosen ? StyleNames.Stored(chosen) : null,
        };
        return ProfileEditError.None;
    }

    private static ProfileEntry ToEntry(string processName, AppProfile profile, IReadOnlyDictionary<string, AppKind>? moved)
    {
        string? cleanup = profile.Cleanup?.Trim().ToLowerInvariant();
        string? formatting = profile.Formatting?.Trim().ToLowerInvariant();
        string tone = profile.Tone?.Trim() ?? string.Empty;
        string instructions = profile.Instructions?.Trim() ?? string.Empty;

        // The Tone box became "How I write here" (ticket 56): old Tone text shows there, and the next
        // save stores it as the Profile's instructions. "none" is a setting, not words, so it stays.
        if (instructions.Length == 0 && tone.Length > 0 && !tone.Equals(AppProfile.None, StringComparison.OrdinalIgnoreCase))
        {
            (instructions, tone) = (tone, string.Empty);
        }

        return new ProfileEntry(
            processName,
            string.IsNullOrWhiteSpace(profile.App) ? DisplayName(processName) : profile.App.Trim(),
            tone,
            // Unset or unknown both mean the built-in choice to transcription (GeminiPrompt), so
            // that is what the editor shows.
            formatting is not null && Formattings.Contains(formatting) ? formatting : FormattingHintMapper.BuiltIn(processName),
            CleanupLevels.IsValid(cleanup) ? cleanup! : DefaultCleanup,
            profile.EmailLayout ?? BuiltInEmailLayout(processName),
            instructions,
            StyleNames.Parse(profile.Style) is { } style ? StyleNames.Stored(style) : string.Empty,
            AppKinds.Of(processName, moved: moved));
    }

    /// <summary>Email apps are laid out as emails without a Profile (ticket 55).</summary>
    private static bool BuiltInEmailLayout(string processName) => AppKinds.Of(processName) == AppKind.Email;

    /// <summary>"notepad" reads as "Notepad" where nothing better is known.</summary>
    private static string DisplayName(string processName) =>
        processName.Length == 0 ? processName : char.ToUpperInvariant(processName[0]) + processName[1..];

    private static bool SameApp(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private AppProfileTable ReadTable() => AppProfileTable.Parse(ReadText());

    private Dictionary<string, AppProfile> ReadProfiles() =>
        ReadTable().Entries.ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);

    private void Write(Dictionary<string, AppProfile> profiles)
    {
        IReadOnlyList<string> comment = AppProfileTable.ExtractComment(ReadText());
        List<KeyValuePair<string, AppProfile>> ordered = [.. profiles.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)];
        AtomicFile.WriteAllBytes(profilesPath, Encoding.UTF8.GetBytes(AppProfileTable.Format(comment, ordered)));
    }

    private string ReadText() => File.Exists(profilesPath) ? File.ReadAllText(profilesPath) : string.Empty;
}
