using VoiceCtrl.Core.Audio;

namespace VoiceCtrl.Core.Tests.Audio;

/// <summary>"No audio from the microphone" (VED-75): a clip that is digital silence, which is what
/// a muted or disconnected-but-still-open device delivers. Room tone and quiet speech are not it.</summary>
public class AudioClipSignalTests
{
    private static byte[] Wav(TimeSpan length, Func<int, short> sample)
    {
        int count = (int)(16000 * length.TotalSeconds);
        byte[] bytes = new byte[44 + 2 * count];
        for (int i = 0; i < count; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(44 + 2 * i), sample(i));
        }

        return bytes;
    }

    [Fact]
    public void AllZeroSamples_AreNoSignal()
    {
        var clip = new AudioClip(Wav(TimeSpan.FromSeconds(1), _ => 0), TimeSpan.FromSeconds(1));

        Assert.True(clip.HasNoSignal());
    }

    [Fact]
    public void ADitherOfAFewLeastSignificantBits_IsStillNoSignal()
    {
        var clip = new AudioClip(Wav(TimeSpan.FromSeconds(1), i => (short)(i % 7 - 3)), TimeSpan.FromSeconds(1));

        Assert.True(clip.HasNoSignal());
    }

    [Fact]
    public void RoomTone_IsSignal()
    {
        // The measured room-tone peak was 418 of 32767 (see IsLikelySilent): far above the floor.
        var clip = new AudioClip(Wav(TimeSpan.FromSeconds(1), i => (short)(i % 2 == 0 ? 418 : -418)), TimeSpan.FromSeconds(1));

        Assert.False(clip.HasNoSignal());
    }

    [Fact]
    public void OneLoudSample_InAnOtherwiseSilentClip_IsSignal()
    {
        var clip = new AudioClip(Wav(TimeSpan.FromSeconds(1), i => (short)(i == 9000 ? -500 : 0)), TimeSpan.FromSeconds(1));

        Assert.False(clip.HasNoSignal());
    }

    [Fact]
    public void ATooShortClip_IsNotJudged()
    {
        var clip = new AudioClip(Wav(TimeSpan.FromMilliseconds(100), _ => 0), TimeSpan.FromMilliseconds(100));

        Assert.False(clip.HasNoSignal());
    }

    [Fact]
    public void BytesThatAreNotAWav_AreNotJudged()
    {
        var clip = new AudioClip([1], TimeSpan.FromSeconds(1));

        Assert.False(clip.HasNoSignal());
    }
}
