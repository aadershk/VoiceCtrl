using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Tests.Transcription;

/// <summary>The request deadline grows with the recording, so a 20-minute clip (VED-76) is not
/// failed by the flat 30 s a sentence gets.</summary>
public class GeminiRequestTimeoutTests
{
    private const int BytesPerSecond = 32_000;

    [Fact]
    public void AShortClip_KeepsTheOld30sDeadlinePlusAHair()
    {
        Assert.Equal(TimeSpan.FromSeconds(35), GeminiTranscriptionClient.RequestTimeoutFor(10 * BytesPerSecond));
    }

    [Fact]
    public void TheLongestRecording_GetsAboutTenMinutes()
    {
        TimeSpan timeout = GeminiTranscriptionClient.RequestTimeoutFor(20 * 60 * BytesPerSecond);

        Assert.Equal(TimeSpan.FromSeconds(630), timeout);
    }
}
