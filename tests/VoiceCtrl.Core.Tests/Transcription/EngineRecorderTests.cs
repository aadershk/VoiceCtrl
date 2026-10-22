using System.IO;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public sealed class EngineRecorderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"voicectrl-test-engine-{Guid.NewGuid():N}");
    private bool _enabled = true;

    private string FilePath => Path.Combine(_dir, "engine-record.json");

    private EngineRecorder NewRecorder() => new(FilePath, () => _enabled, () => new DateTimeOffset(2026, 10, 18, 9, 30, 0, TimeSpan.Zero));

    private static EngineEntry Entry(int totalMs = 1200, params WordPair[] mended) => new(
        At: new DateTimeOffset(2026, 10, 18, 9, 30, 0, TimeSpan.Zero),
        Mend: "ran",
        MendProposed: mended.Length,
        MendRefused: 0,
        MendApplied: mended,
        SmartCleanup: "applied",
        GuardRejections: [],
        Vocabulary: [],
        ModelMs: 800,
        TotalMs: totalMs);

    [Fact]
    public void Off_BeginGivesNoTrace_AndAddWritesNothing()
    {
        _enabled = false;
        EngineRecorder recorder = NewRecorder();

        Assert.Null(recorder.Begin());
        recorder.Add(Entry());

        Assert.Empty(recorder.Entries);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void On_AnEntrySurvivesAReopen()
    {
        NewRecorder().Add(Entry(1500, new WordPair("hole", "whole")));

        EngineEntry saved = Assert.Single(NewRecorder().Entries);

        Assert.Equal(1500, saved.TotalMs);
        Assert.Equal(new WordPair("hole", "whole"), Assert.Single(saved.MendApplied));
    }

    [Fact]
    public void KeepsOnlyTheLast500_DroppingTheOldest()
    {
        EngineRecorder recorder = NewRecorder();

        for (int i = 1; i <= EngineRecorder.MaxEntries + 20; i++)
        {
            recorder.Add(Entry(totalMs: i));
        }

        IReadOnlyList<EngineEntry> entries = NewRecorder().Entries;
        Assert.Equal(EngineRecorder.MaxEntries, entries.Count);
        Assert.Equal(21, entries[0].TotalMs);
        Assert.Equal(EngineRecorder.MaxEntries + 20, entries[^1].TotalMs);
    }

    [Fact]
    public void Clear_DeletesTheFile_AndTheEntries()
    {
        EngineRecorder recorder = NewRecorder();
        recorder.Add(Entry());
        Assert.True(File.Exists(FilePath));

        recorder.Clear();

        Assert.False(File.Exists(FilePath));
        Assert.Empty(recorder.Entries);
        Assert.Empty(NewRecorder().Entries);
    }

    [Fact]
    public void SwitchedOffWhileADictationRuns_TheLateEntryIsDropped()
    {
        EngineRecorder recorder = NewRecorder();
        EngineTrace? trace = recorder.Begin();
        Assert.NotNull(trace);

        _enabled = false;
        recorder.Clear();
        recorder.Add(Entry());

        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void ClearIfOff_RemovesAFileLeftBehind_ButKeepsItWhileOn()
    {
        NewRecorder().Add(Entry());

        NewRecorder().ClearIfOff();
        Assert.True(File.Exists(FilePath));

        _enabled = false;
        NewRecorder().ClearIfOff();
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void ACorruptFile_ReadsAsEmpty_AndIsReplacedByTheNextEntry()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");
        EngineRecorder recorder = NewRecorder();

        Assert.Empty(recorder.Entries);
        recorder.Add(Entry());

        Assert.Single(NewRecorder().Entries);
    }

    [Fact]
    public void WordsThatAreNotSingleWords_NeverReachTheFile()
    {
        // The record holds pairs of words, never a sentence: anything else is dropped on the way in.
        EngineEntry sneaky = Entry(1000, new WordPair("hole", "whole"), new WordPair("book the table for six", "x"), new WordPair("a@b.com", "y"));

        EngineRecorder recorder = NewRecorder();
        recorder.Add(sneaky);

        Assert.Equal(new WordPair("hole", "whole"), Assert.Single(Assert.Single(recorder.Entries).MendApplied));
        string json = File.ReadAllText(FilePath);
        Assert.DoesNotContain("table", json);
        Assert.DoesNotContain("a@b.com", json);
    }

    [Fact]
    public void ALine_HasCountsPairsAndTimings_AndNothingElse()
    {
        EngineEntry entry = new(
            At: new DateTimeOffset(2026, 10, 18, 9, 30, 0, TimeSpan.Zero),
            Mend: "ran",
            MendProposed: 3,
            MendRefused: 2,
            MendApplied: [new WordPair("sex", "six")],
            SmartCleanup: "guard rejected",
            GuardRejections: ["invented word"],
            Vocabulary: [new WordPair("skippel", "Schiphol")],
            ModelMs: 2600,
            TotalMs: 3900);

        string line = entry.ToLine();

        Assert.Equal(
            "2026-10-18 09:30 | mend: ran, proposed 3, refused 2, applied sex -> six | smart cleanup: guard rejected (invented word) | vocabulary: skippel -> Schiphol | model 2600 ms | total 3900 ms",
            line);
    }

    [Fact]
    public void ALineForAQuietDictation_SaysNone()
    {
        EngineEntry entry = Entry(900) with { Mend = "skipped", MendProposed = 0, SmartCleanup = "gated off", ModelMs = 0 };

        Assert.Equal(
            "2026-10-18 09:30 | mend: skipped | smart cleanup: gated off | vocabulary: none | model 0 ms | total 900 ms",
            entry.ToLine());
    }

    [Theory]
    [InlineData("guard: invented word 'Priya'", "invented word")]
    [InlineData("guard: invented number '42'", "invented number")]
    [InlineData("guard: dropped 'tomorrow'", "dropped word")]
    [InlineData("guard: word for word, but 'hello there' was removed", "word for word")]
    [InlineData("guard: length ratio 0.40 outside 0.7-1.3", "length ratio")]
    [InlineData("guard: too many reworded words", "too many reworded words")]
    [InlineData("guard: preamble or code fence", "preamble")]
    [InlineData("guard: something new 'Secret'", "other")]
    public void AGuardReason_BecomesAKindWithoutTheWords(string reason, string kind) =>
        Assert.Equal(kind, EngineTrace.GuardKind(reason));

    [Theory]
    [InlineData(null, "applied", 700)]
    [InlineData("gate: nothing to improve", "gated off", 0)]
    [InlineData("no model", "not available", 0)]
    [InlineData("timed out", "timed out", 700)]
    [InlineData("model failed: HttpRequestException", "failed", 700)]
    [InlineData("guard: dropped 'x'", "guard rejected", 700)]
    public void TheCleanupResult_BecomesAStatus_AndOnlyACalledModelCountsAsModelTime(string? reason, string status, int modelMs)
    {
        var trace = new EngineTrace();

        trace.RecordCleanup(new SmartCleanupResult("t", reason is null, reason), elapsedMs: 700);

        Assert.Equal(status, trace.SmartCleanup);
        Assert.Equal(modelMs, trace.ModelMs);
    }

    [Fact]
    public void AGuardRejection_IsRecordedByKind_NotByWord()
    {
        var trace = new EngineTrace();

        trace.RecordCleanup(new SmartCleanupResult("t", false, "guard: invented word 'Priya'"), elapsedMs: 100);

        Assert.Equal(["invented word"], trace.GuardRejections);
        Assert.DoesNotContain("Priya", trace.ToEntry(DateTimeOffset.UnixEpoch, 10).ToLine());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
