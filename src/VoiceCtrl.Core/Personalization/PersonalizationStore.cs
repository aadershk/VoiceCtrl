using VoiceCtrl.Core.Config;

namespace VoiceCtrl.Core.Personalization;

/// <summary>
/// The three user-editable files, behind one object the transcription clients can hold. Each is
/// read lazily and reparsed only when it changes, so editing a dictionary term takes effect on the
/// next dictation without restarting the app, and an untouched install pays a few stat calls.
/// </summary>
public sealed class PersonalizationStore
{
    /// <summary>For callers that have no user files at all: tests, and the benchmark harness,
    /// which must measure the transcription path rather than whatever is in the real dictionary.</summary>
    public static PersonalizationStore Empty { get; } = new();

    private readonly UserFileCache<IReadOnlyList<DictionaryEntry>>? _dictionary;
    private readonly UserFileCache<SnippetTable>? _snippets;
    private readonly UserFileCache<AppProfileTable>? _profiles;

    private PersonalizationStore()
    {
    }

    public PersonalizationStore(string dictionaryPath, string snippetsPath, string profilesPath)
    {
        _dictionary = new UserFileCache<IReadOnlyList<DictionaryEntry>>(
            dictionaryPath, contents => CustomDictionary.Parse(SplitLines(contents)), Array.Empty<DictionaryEntry>());

        _snippets = new UserFileCache<SnippetTable>(
            snippetsPath, contents => SnippetTable.Parse(SplitLines(contents)), SnippetTable.Empty);

        _profiles = new UserFileCache<AppProfileTable>(
            profilesPath, AppProfileTable.Parse, AppProfileTable.Empty);
    }

    public IReadOnlyList<DictionaryEntry> Dictionary => _dictionary?.Value ?? Array.Empty<DictionaryEntry>();

    public SnippetTable Snippets => _snippets?.Value ?? SnippetTable.Empty;

    public AppProfileTable Profiles => _profiles?.Value ?? AppProfileTable.Empty;

    /// <summary>
    /// Creates the three files with commented starter content if they do not exist, then reads
    /// from them. Seeding rather than documenting: a file that is already there and already
    /// explains itself is the only version of this a user actually discovers.
    /// </summary>
    public static PersonalizationStore CreateDefault() => new(
        UserDataPaths.EnsureSeeded(UserDataPaths.Dictionary, CustomDictionary.SeedContents),
        UserDataPaths.EnsureSeeded(UserDataPaths.Snippets, SnippetTable.SeedContents),
        UserDataPaths.EnsureSeeded(UserDataPaths.Profiles, AppProfileTable.SeedContents));

    private static string[] SplitLines(string contents) => contents.Split('\n');
}
