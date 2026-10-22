using System.Text.Json;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Dictation;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Tests.Bridge;

public class DictationsContractsTests
{
    [Fact]
    public void View_CarriesTimesAsUnixMilliseconds_AndAudioAsWholeSecondsOfAtLeastOne()
    {
        var at = new DateTimeOffset(2026, 9, 29, 9, 41, 0, TimeSpan.Zero);
        var record = new DictationRecord { Text = "hi", TimestampUtc = at.UtcDateTime, AppName = "Slack" };
        var failed = new FailedDictation(Guid.NewGuid(), [], TimeSpan.FromMilliseconds(300), FailureReason.NoInternet, at, null);

        DictationsView view = DictationsView.From(KeepDictations.Last50, [record], [failed], canRetryOffline: true, ["Ctrl", "Shift", "V"]);

        Assert.Equal(at.ToUnixTimeMilliseconds(), view.Dictations[0].AtMs);
        Assert.Equal("Slack", view.Dictations[0].App);
        Assert.Equal(1, view.Failed[0].AudioSeconds);
        Assert.Equal(at.ToUnixTimeMilliseconds(), view.Failed[0].AtMs);
    }

    [Fact]
    public void View_TravelsAsCamelCase()
    {
        DictationsView view = DictationsView.From(KeepDictations.Last24Hours, [], [], false, ["Ctrl", "Shift", "V"]);

        string json = JsonSerializer.Serialize(view, BridgeJsonContext.Default.DictationsView);

        Assert.Contains("\"keep\":\"last24Hours\"", json);
        Assert.Contains("\"pasteAgainKeys\":[\"Ctrl\",\"Shift\",\"V\"]", json);
    }

    [Fact]
    public void AMalformedId_FromThePage_IsRejected()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize("{\"id\":\"not-a-guid\"}", BridgeJsonContext.Default.DeleteDictation));
    }
}
