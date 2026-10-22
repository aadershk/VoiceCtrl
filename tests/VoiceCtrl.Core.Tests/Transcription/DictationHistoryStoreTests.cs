using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public class DictationHistoryStoreTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 29, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void NoFile_StartsEmpty()
    {
        DictationHistoryStore store = Load(TempHistoryPath());

        Assert.Empty(store.Entries);
    }

    [Fact]
    public void Add_PersistsTextTimeAndApp_AcrossReload()
    {
        string path = TempHistoryPath();
        DictationHistoryStore store = Load(path);

        store.Add("hello world", "Outlook");

        DictationRecord reloaded = Assert.Single(Load(path).Entries);
        Assert.Equal("hello world", reloaded.Text);
        Assert.Equal("Outlook", reloaded.AppName);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, reloaded.TimestampUtc);
        Assert.Equal(store.Entries[0].Id, reloaded.Id);
    }

    [Fact]
    public void Add_KeepsTheOriginalBesideTheFinalText_AcrossReload()
    {
        string path = TempHistoryPath();
        DictationHistoryStore store = Load(path);

        store.Add("Send it to Sarah.", "Outlook", original: "Send it to John, actually, make that Sarah.");

        DictationRecord reloaded = Assert.Single(Load(path).Entries);
        Assert.Equal("Send it to Sarah.", reloaded.Text);
        Assert.Equal("Send it to John, actually, make that Sarah.", reloaded.Original);
    }

    [Fact]
    public void ADictationSmartCleanupLeftAlone_HasNoOriginal()
    {
        string path = TempHistoryPath();
        DictationHistoryStore store = Load(path);

        store.Add("hello world", "Outlook");

        Assert.Null(store.Entries[0].Original);
        Assert.DoesNotContain("Original", File.ReadAllText(path));
    }

    [Fact]
    public void RestoreOriginal_PutsTheOriginalBackAsTheText_AndKeepsIdTimeAndApp()
    {
        string path = TempHistoryPath();
        DictationHistoryStore store = Load(path);
        store.Add("Send it to Sarah.", "Outlook", original: "Send it to John, actually, make that Sarah.");
        DictationRecord before = store.Entries[0];
        int changes = 0;
        store.Changed += () => changes++;

        bool restored = store.RestoreOriginal(before.Id);

        Assert.True(restored);
        DictationRecord after = Assert.Single(Load(path).Entries);
        Assert.Equal("Send it to John, actually, make that Sarah.", after.Text);
        Assert.Null(after.Original);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.TimestampUtc, after.TimestampUtc);
        Assert.Equal("Outlook", after.AppName);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void RestoreOriginal_WithNothingToRestore_DoesNothing()
    {
        DictationHistoryStore store = Load(TempHistoryPath());
        store.Add("hello world", "Outlook");

        Assert.False(store.RestoreOriginal(store.Entries[0].Id));
        Assert.False(store.RestoreOriginal(Guid.NewGuid()));
        Assert.Equal("hello world", store.Entries[0].Text);
    }

    [Fact]
    public void V3Records_WithNoIdOrApp_StillLoad_AndGetAStableId()
    {
        string path = TempHistoryPath();
        File.WriteAllText(path, """[{"Text":"from v3","TimestampUtc":"2026-09-20T08:00:00Z"}]""");

        DictationHistoryStore store = Load(path);

        DictationRecord record = Assert.Single(store.Entries);
        Assert.Equal("from v3", record.Text);
        Assert.Null(record.AppName);
        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal(record.Id, store.Entries[0].Id);
    }

    [Fact]
    public void Entries_AreNewestFirst()
    {
        DictationHistoryStore store = Load(TempHistoryPath());

        store.Add("first");
        store.Add("second");

        Assert.Equal(["second", "first"], store.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void Last50_DropsTheOldestPastFifty()
    {
        DictationHistoryStore store = Load(TempHistoryPath());

        for (int i = 0; i < DictationHistoryStore.MaxEntries + 1; i++)
        {
            store.Add($"entry {i}");
        }

        Assert.Equal(DictationHistoryStore.MaxEntries, store.Entries.Count);
        Assert.DoesNotContain(store.Entries, entry => entry.Text == "entry 0");
        Assert.Contains(store.Entries, entry => entry.Text == $"entry {DictationHistoryStore.MaxEntries}");
    }

    [Fact]
    public void Last24Hours_DropsWhatIsOlderThanADay_AndIsNotCappedAtFifty()
    {
        string path = TempHistoryPath();
        DictationHistoryStore store = Load(path, KeepDictations.Last24Hours);
        store.Add("yesterday morning");
        _clock.Advance(TimeSpan.FromHours(2));
        for (int i = 0; i < DictationHistoryStore.MaxEntries; i++)
        {
            store.Add($"entry {i}");
        }

        Assert.Equal(DictationHistoryStore.MaxEntries + 1, store.Entries.Count);

        _clock.Advance(TimeSpan.FromHours(23));

        Assert.Equal(DictationHistoryStore.MaxEntries, store.Entries.Count);
        Assert.DoesNotContain(store.Entries, entry => entry.Text == "yesterday morning");
        Assert.Equal(DictationHistoryStore.MaxEntries, Load(path, KeepDictations.Last24Hours).Entries.Count);
    }

    [Fact]
    public void None_KeepsNothingOnDisk_AndAddIsIgnored()
    {
        string path = TempHistoryPath();
        DictationHistoryStore store = Load(path, KeepDictations.None);

        store.Add("not kept");

        Assert.Empty(store.Entries);
        Assert.Empty(Load(path).Entries);
    }

    [Fact]
    public void Loading_WithNone_ErasesWhatAnEarlierRunKept()
    {
        string path = TempHistoryPath();
        Load(path).Add("kept earlier");

        Load(path, KeepDictations.None);

        Assert.Empty(Load(path).Entries);
    }

    [Fact]
    public void ChangingKeep_PrunesAtOnce_PersistsAndRaisesChanged()
    {
        string path = TempHistoryPath();
        DictationHistoryStore store = Load(path);
        store.Add("two days ago");
        _clock.Advance(TimeSpan.FromDays(2));
        store.Add("just now");
        int raised = 0;
        store.Changed += () => raised++;

        store.Keep = KeepDictations.Last24Hours;

        Assert.Equal("just now", Assert.Single(store.Entries).Text);
        Assert.Single(Load(path, KeepDictations.Last50).Entries);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Remove_DeletesByIdAndPersists_AndUndoPutsItBackInPlace()
    {
        string path = TempHistoryPath();
        DictationHistoryStore store = Load(path);
        store.Add("oldest");
        _clock.Advance(TimeSpan.FromMinutes(1));
        store.Add("middle");
        _clock.Advance(TimeSpan.FromMinutes(1));
        store.Add("newest");
        Guid middle = store.Entries[1].Id;

        Assert.True(store.Remove(middle));

        Assert.Equal(["newest", "oldest"], store.Entries.Select(e => e.Text));
        Assert.Equal(2, Load(path).Entries.Count);

        Assert.True(store.Undo(middle));

        Assert.Equal(["newest", "middle", "oldest"], store.Entries.Select(e => e.Text));
        Assert.Equal(middle, Load(path).Entries[1].Id);
    }

    [Fact]
    public void Remove_And_Undo_OfAnUnknownId_DoNothing()
    {
        DictationHistoryStore store = Load(TempHistoryPath());
        store.Add("kept");
        int raised = 0;
        store.Changed += () => raised++;

        Assert.False(store.Remove(Guid.NewGuid()));
        Assert.False(store.Undo(store.Entries[0].Id));
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Undo_WorksOnlyOnce()
    {
        DictationHistoryStore store = Load(TempHistoryPath());
        store.Add("once");
        Guid id = store.Entries[0].Id;
        store.Remove(id);

        Assert.True(store.Undo(id));
        Assert.False(store.Undo(id));
        Assert.Single(store.Entries);
    }

    [Fact]
    public void Clear_RemovesEverything_Persists_AndCannotBeUndone()
    {
        string path = TempHistoryPath();
        DictationHistoryStore store = Load(path);
        store.Add("one");
        store.Add("two");
        Guid id = store.Entries[0].Id;

        store.Clear();

        Assert.Empty(store.Entries);
        Assert.Empty(Load(path).Entries);
        Assert.False(store.Undo(id));
    }

    [Fact]
    public void CorruptFile_TreatedAsEmpty()
    {
        string path = TempHistoryPath();
        File.WriteAllText(path, "{not valid json");

        DictationHistoryStore store = Load(path);

        Assert.Empty(store.Entries);
    }

    [Fact]
    public void EveryChange_RaisesChangedOnce()
    {
        DictationHistoryStore store = Load(TempHistoryPath());
        int raised = 0;
        store.Changed += () => raised++;

        store.Add("hello world");
        Guid id = store.Entries[0].Id;
        store.Remove(id);
        store.Undo(id);
        store.Clear();

        Assert.Equal(4, raised);
    }

    [Fact]
    public void ForTesting_NeverTouchesDisk()
    {
        DictationHistoryStore store = DictationHistoryStore.ForTesting();

        store.Add("in memory only");

        Assert.Single(store.Entries);
    }

    [Fact]
    public void WouldRemove_CountsWhatAShorterChoiceDeletes_WithoutDeletingIt()
    {
        DictationHistoryStore store = DictationHistoryStore.ForTesting(clock: _clock);
        store.Add("two days ago");
        store.Add("also old");
        _clock.Advance(TimeSpan.FromHours(48));
        store.Add("just now");

        Assert.Equal(3, store.WouldRemove(KeepDictations.None));
        Assert.Equal(2, store.WouldRemove(KeepDictations.Last24Hours));
        Assert.Equal(0, store.WouldRemove(KeepDictations.Last50));
        Assert.Equal(3, store.Entries.Count);
    }

    private DictationHistoryStore Load(string path, KeepDictations keep = KeepDictations.Last50) =>
        DictationHistoryStore.Load(path, keep, _clock);

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

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
