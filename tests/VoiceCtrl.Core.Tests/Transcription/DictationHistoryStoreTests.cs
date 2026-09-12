using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public class DictationHistoryStoreTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    [Fact]
    public void NoFile_StartsEmpty()
    {
        DictationHistoryStore store = DictationHistoryStore.Load(TempHistoryPath());

        Assert.Empty(store.Entries);
    }

    [Fact]
    public void Add_PersistsAcrossReload()
    {
        string path = TempHistoryPath();
        DictationHistoryStore store = DictationHistoryStore.Load(path);

        store.Add("hello world");

        DictationHistoryStore reloaded = DictationHistoryStore.Load(path);
        Assert.Equal("hello world", Assert.Single(reloaded.Entries).Text);
    }

    [Fact]
    public void Entries_AreNewestFirst()
    {
        DictationHistoryStore store = DictationHistoryStore.Load(TempHistoryPath());

        store.Add("first");
        store.Add("second");

        Assert.Equal(["second", "first"], store.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void Add_BeyondMaxEntries_DropsOldest()
    {
        DictationHistoryStore store = DictationHistoryStore.Load(TempHistoryPath());

        for (int i = 0; i < DictationHistoryStore.MaxEntries + 1; i++)
        {
            store.Add($"entry {i}");
        }

        Assert.Equal(DictationHistoryStore.MaxEntries, store.Entries.Count);
        Assert.DoesNotContain(store.Entries, entry => entry.Text == "entry 0");
        Assert.Contains(store.Entries, entry => entry.Text == $"entry {DictationHistoryStore.MaxEntries}");
    }

    [Fact]
    public void Search_IsCaseInsensitiveSubstringMatch()
    {
        DictationHistoryStore store = DictationHistoryStore.Load(TempHistoryPath());
        store.Add("Remember to call the Vet");
        store.Add("Pick up groceries");

        IReadOnlyList<DictationRecord> results = store.Search("vet");

        Assert.Equal("Remember to call the Vet", Assert.Single(results).Text);
    }

    [Fact]
    public void Search_WhitespaceQuery_ReturnsAllEntries()
    {
        DictationHistoryStore store = DictationHistoryStore.Load(TempHistoryPath());
        store.Add("one");
        store.Add("two");

        Assert.Equal(2, store.Search("   ").Count);
    }

    [Fact]
    public void CorruptFile_TreatedAsEmpty()
    {
        string path = TempHistoryPath();
        File.WriteAllText(path, "{not valid json");

        DictationHistoryStore store = DictationHistoryStore.Load(path);

        Assert.Empty(store.Entries);
    }

    [Fact]
    public void Remove_DeletesEntryAndPersists()
    {
        string path = TempHistoryPath();
        DictationHistoryStore store = DictationHistoryStore.Load(path);
        store.Add("keep me");
        store.Add("delete me");
        DictationRecord toDelete = store.Entries.Single(entry => entry.Text == "delete me");

        store.Remove(toDelete);

        Assert.Equal("keep me", Assert.Single(store.Entries).Text);
        DictationHistoryStore reloaded = DictationHistoryStore.Load(path);
        Assert.Equal("keep me", Assert.Single(reloaded.Entries).Text);
    }

    [Fact]
    public void Add_RaisesChanged()
    {
        DictationHistoryStore store = DictationHistoryStore.Load(TempHistoryPath());
        int raised = 0;
        store.Changed += () => raised++;

        store.Add("hello world");

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Remove_RaisesChanged()
    {
        DictationHistoryStore store = DictationHistoryStore.Load(TempHistoryPath());
        store.Add("delete me");
        int raised = 0;
        store.Changed += () => raised++;

        store.Remove(store.Entries.Single());

        Assert.Equal(1, raised);
    }

    [Fact]
    public void ForTesting_NeverTouchesDisk()
    {
        DictationHistoryStore store = DictationHistoryStore.ForTesting();

        store.Add("in memory only");

        Assert.Single(store.Entries);
    }

    private string TempHistoryPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"voicectrl-test-history-{Guid.NewGuid()}.json");
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (string path in _tempFiles)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
