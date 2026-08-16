using VoiceCtrl.Core.Audio;
using Xunit;

namespace VoiceCtrl.Core.Tests.Audio;

public class AudioClipTests
{
    [Fact]
    public void DurationBelowMinimum_IsLikelySilent()
    {
        var clip = new AudioClip(Array.Empty<byte>(), TimeSpan.FromMilliseconds(299));
        Assert.True(clip.IsLikelySilent());
    }

    [Fact]
    public void DurationAtMinimum_IsNotLikelySilent()
    {
        var clip = new AudioClip(Array.Empty<byte>(), TimeSpan.FromMilliseconds(300));
        Assert.False(clip.IsLikelySilent());
    }

    [Fact]
    public void DurationAboveMinimum_IsNotLikelySilent_RegardlessOfAmplitude()
    {
        // A real-hardware measurement showed room tone can out-peak genuine speech (see
        // AudioClip.IsLikelySilent doc comment). Amplitude is deliberately not a factor here.
        byte[] quietBytes = new byte[2000];
        var clip = new AudioClip(quietBytes, TimeSpan.FromSeconds(2));
        Assert.False(clip.IsLikelySilent());
    }
}
