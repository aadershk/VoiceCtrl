using System.IO;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Diagnostics;
using Xunit;

namespace VoiceCtrl.Core.Tests.Diagnostics;

public class DiagnosticsReportTests
{
    private const string Profile = @"C:\Users\Aader";
    private const string GeminiKey = "AIzaSyD4f9Kq2LmN8pQrStUvWxYz0123456789ab";
    private const string Dictation = "Book the Rijksmuseum tour for Priya on Friday";

    private static readonly LogScrubber Scrubber = new(Profile, "Aader", [GeminiKey]);

    private static DiagnosticsFacts Facts(IReadOnlyList<string> log, UserSettings? settings = null) => new(
        AppVersion: "4.0.0",
        OsDescription: "Microsoft Windows 10.0.26200",
        WebView2Version: "131.0.2903.70",
        Settings: settings ?? new UserSettings(),
        Online: OnlinePresets.For(OnlineProvider.Nvidia, new OtherProviderSettings()),
        SavedProviderKeys: [OnlineProvider.Gemini],
        TypeSafeKeySaved: false,
        PasteAgainTaken: false,
        DictionaryTerms: 12,
        Snippets: 3,
        Profiles: 2,
        KeptDictations: 41,
        LogLines: log,
        CreatedAt: new DateTimeOffset(2026, 9, 30, 9, 41, 0, TimeSpan.FromHours(2)));

    [Fact]
    public void Build_WithASeededLog_LetsNoKeyDictationOrUserPathOut()
    {
        string[] log =
        [
            "2026-09-30 09:40:01 INFO  VoiceCtrl 4.0.0 starting",
            $"2026-09-30 09:40:02 ERROR [Gemini] GeminiApiException: 400 for https://generativelanguage.googleapis.com/v1beta?key={GeminiKey}",
            $"2026-09-30 09:40:03 INFO  Transcribed \"{Dictation}\"",
            $@"2026-09-30 09:40:04 ERROR [History] IOException: {Profile}\AppData\Local\VoiceCtrl\history.json is locked",
        ];
        UserSettings settings = new UserSettings() with
        {
            Dictation = new DictationSection { MicrophoneId = "{0.0.1.00000000}.{9f1c}", MicrophoneName = @"Headset (C:\Users\Aader)" },
            Advanced = new AdvancedSection { StyleNotes = "I work at Acme on the Priya account" },
        };

        string report = DiagnosticsReport.Build(Facts(log, settings), Scrubber);

        Assert.DoesNotContain(GeminiKey, report);
        Assert.DoesNotContain("Rijksmuseum", report);
        Assert.DoesNotContain("Priya", report);
        Assert.DoesNotContain("Aader", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9f1c", report);
        Assert.Contains(@"%USERPROFILE%\AppData\Local\VoiceCtrl\history.json is locked", report);
        Assert.Contains("VoiceCtrl 4.0.0 starting", report);
    }

    [Fact]
    public void Build_SaysWhatAMaintainerNeeds_AndOnlyCountsPersonalization()
    {
        string report = DiagnosticsReport.Build(Facts(["2026-09-30 09:40:01 INFO  hello"]), Scrubber);

        Assert.Contains("VoiceCtrl 4.0.0", report);
        Assert.Contains("Microsoft Windows 10.0.26200", report);
        Assert.Contains("WebView2 131.0.2903.70", report);
        Assert.Contains("transcription.mode = auto", report);
        Assert.Contains("overlay.dock = bottom", report);
        Assert.Contains("Gemini key: saved", report);
        Assert.Contains("NVIDIA key: not saved", report);
        Assert.Contains($"Online provider: NVIDIA (Dictation {OnlinePresets.NvidiaAudioModel}; text {OnlinePresets.NvidiaTextModel}, then {OnlinePresets.NvidiaTextBackup})", report);
        Assert.Contains("TypeSafe key: not saved", report);
        Assert.Contains("Dictionary words: 12", report);
        Assert.Contains("Snippets: 3", report);
        Assert.Contains("Profiles: 2", report);
        Assert.Contains("Kept Dictations: 41", report);
        Assert.Contains("hello", report);
    }

    [Fact]
    public void Build_NamesTheMicrophone_NotItsId_AndHidesStyleNotes()
    {
        UserSettings settings = new UserSettings() with
        {
            Dictation = new DictationSection { MicrophoneId = "{0.0.1.00000000}.{abc}", MicrophoneName = "Jabra Evolve2" },
            Advanced = new AdvancedSection { StyleNotes = "British spelling" },
        };

        string report = DiagnosticsReport.Build(Facts([], settings), Scrubber);

        Assert.Contains("dictation.microphoneName = Jabra Evolve2", report);
        Assert.DoesNotContain("microphoneId", report);
        Assert.Contains("advanced.styleNotes = (set)", report);
        Assert.DoesNotContain("British", report);
    }

    [Fact]
    public void Build_WithTheEngineRecordOff_HasNoSectionForIt()
    {
        string report = DiagnosticsReport.Build(Facts(["hello"]), Scrubber);

        Assert.DoesNotContain("Engine record", report);
    }

    [Fact]
    public void Build_WithTheEngineRecordOn_ListsItsLines_ThroughTheScrubber()
    {
        string[] lines =
        [
            "2026-10-18 09:30 | mend: ran, proposed 1, refused 0, applied sex -> six | smart cleanup: applied | vocabulary: none | model 2600 ms | total 3900 ms",
            @"2026-10-18 09:31 | mend: ran, applied aader -> C:\Users\Aader | smart cleanup: gated off | vocabulary: none | model 0 ms | total 100 ms",
        ];

        string report = DiagnosticsReport.Build(Facts([]) with { EngineRecordLines = lines }, Scrubber);

        Assert.Contains("## Engine record (2 Dictations)", report);
        Assert.Contains("applied sex -> six", report);
        Assert.DoesNotContain("Aader", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WithTheEngineRecordOnButEmpty_SaysNothingIsRecordedYet()
    {
        string report = DiagnosticsReport.Build(Facts([]) with { EngineRecordLines = [] }, Scrubber);

        Assert.Contains("## Engine record (0 Dictations)", report);
        Assert.Contains("Nothing recorded yet.", report);
    }

    [Fact]
    public void Build_WithoutWebView2_SaysSo()
    {
        string report = DiagnosticsReport.Build(Facts([]) with { WebView2Version = null }, Scrubber);

        Assert.Contains("WebView2 not found", report);
    }

    [Fact]
    public void LastLines_ReadsOnlyTheTail_AndCopesWithAMissingFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vc-diag-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllLines(path, Enumerable.Range(1, 700).Select(i => $"line {i}"));

            IReadOnlyList<string> lines = DiagnosticsReport.LastLines(path, 500);

            Assert.Equal(500, lines.Count);
            Assert.Equal("line 201", lines[0]);
            Assert.Equal("line 700", lines[^1]);
        }
        finally
        {
            File.Delete(path);
        }

        Assert.Empty(DiagnosticsReport.LastLines(path, 500));
    }
}
