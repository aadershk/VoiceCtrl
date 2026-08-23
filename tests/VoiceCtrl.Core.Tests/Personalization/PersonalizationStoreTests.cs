using System.IO;
using VoiceCtrl.Core.Personalization;
using Xunit;

namespace VoiceCtrl.Core.Tests.Personalization;

public class PersonalizationStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"VoiceCtrlTests_{Path.GetRandomFileName()}");

    public PersonalizationStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Empty_PersonalizesNothing()
    {
        // Every transcription client takes this when no store is supplied, so it has to behave
        // exactly as the app did before personalization existed.
        PersonalizationStore store = PersonalizationStore.Empty;

        Assert.Empty(store.DictionaryTerms);
        Assert.Equal("untouched", store.Snippets.Expand("untouched"));
        Assert.Null(store.Profiles.Resolve("slack"));
    }

    [Fact]
    public void ReadsAllThreeFiles()
    {
        var store = new PersonalizationStore(
            Write("dictionary.txt", "Schiphol\n"),
            Write("snippets.txt", "brb = be right back\n"),
            Write("profiles.json", """{ "slack": { "tone": "casual" } }"""));

        Assert.Equal("Schiphol", Assert.Single(store.DictionaryTerms));
        Assert.Equal("be right back", store.Snippets.Expand("brb"));
        Assert.Equal("casual", store.Profiles.Resolve("slack")?.Tone);
    }

    [Fact]
    public void MissingFiles_LeaveEveryFeatureInactive()
    {
        var store = new PersonalizationStore(
            Path.Combine(_directory, "absent-dictionary.txt"),
            Path.Combine(_directory, "absent-snippets.txt"),
            Path.Combine(_directory, "absent-profiles.json"));

        Assert.Empty(store.DictionaryTerms);
        Assert.Equal("untouched", store.Snippets.Expand("untouched"));
        Assert.Null(store.Profiles.Resolve("slack"));
    }

    [Fact]
    public void PicksUpAnEditWithoutRestarting()
    {
        string dictionaryPath = Write("dictionary.txt", "Schiphol\n");
        var store = new PersonalizationStore(
            dictionaryPath,
            Write("snippets.txt", string.Empty),
            Write("profiles.json", string.Empty));

        Assert.Single(store.DictionaryTerms);

        File.WriteAllText(dictionaryPath, "Schiphol\nGrafana\n");

        Assert.Equal(2, store.DictionaryTerms.Count);
    }

    private string Write(string fileName, string contents)
    {
        string path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, contents);
        return path;
    }
}
