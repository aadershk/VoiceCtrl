using VoiceCtrl.Core.Audio;
using Xunit;

namespace VoiceCtrl.Core.Tests.Audio;

public class AudioLevelMeterTests
{
    [Fact]
    public void SilentFloatBuffer_IsZero()
    {
        byte[] buffer = FloatBuffer([0f, 0f, 0f, 0f]);

        Assert.Equal(0f, AudioLevelMeter.ComputeLevel(buffer, isFloat: true, bitsPerSample: 32));
    }

    [Fact]
    public void SilentPcm16Buffer_IsZero()
    {
        byte[] buffer = Pcm16Buffer([0, 0, 0, 0]);

        Assert.Equal(0f, AudioLevelMeter.ComputeLevel(buffer, isFloat: false, bitsPerSample: 16));
    }

    [Fact]
    public void FullScaleFloatBuffer_IsOne()
    {
        byte[] buffer = FloatBuffer([1f, -1f, 1f, -1f]);

        Assert.Equal(1f, AudioLevelMeter.ComputeLevel(buffer, isFloat: true, bitsPerSample: 32));
    }

    // Not exactly 1: +32767 is one LSB short of full scale against the 32768 divisor, which is
    // the correct asymmetry of two's complement audio rather than an off-by-one to fix.
    [Fact]
    public void FullScalePcm16Buffer_IsOne()
    {
        byte[] buffer = Pcm16Buffer([short.MaxValue, short.MinValue, short.MaxValue, short.MinValue]);

        Assert.Equal(1f, AudioLevelMeter.ComputeLevel(buffer, isFloat: false, bitsPerSample: 16), precision: 4);
    }

    // The two formats have to agree, since which one a machine uses is decided by its audio
    // driver. A meter that reads differently on two machines for the same speech is a bug.
    [Fact]
    public void FloatAndPcm16_AgreeOnTheSameSignal()
    {
        float[] samples = [0.5f, -0.5f, 0.25f, -0.25f];
        short[] equivalent = [16384, -16384, 8192, -8192];

        float fromFloat = AudioLevelMeter.ComputeLevel(FloatBuffer(samples), isFloat: true, bitsPerSample: 32);
        float fromPcm = AudioLevelMeter.ComputeLevel(Pcm16Buffer(equivalent), isFloat: false, bitsPerSample: 16);

        Assert.Equal(fromFloat, fromPcm, precision: 3);
    }

    [Fact]
    public void LouderSignal_ReadsHigher()
    {
        float quiet = AudioLevelMeter.ComputeLevel(FloatBuffer([0.01f, -0.01f]), isFloat: true, bitsPerSample: 32);
        float loud = AudioLevelMeter.ComputeLevel(FloatBuffer([0.3f, -0.3f]), isFloat: true, bitsPerSample: 32);

        Assert.True(loud > quiet, $"expected {loud} > {quiet}");
    }

    // The point of the decibel scale: ordinary speech has to land somewhere visible in the
    // middle of the range, not squashed against zero the way a linear meter would put it.
    [Fact]
    public void SpeechLevelSignal_LandsInTheVisibleMiddleOfTheRange()
    {
        // -26 dBFS, a typical conversational RMS.
        float level = AudioLevelMeter.NormalizeRms(0.05);

        Assert.InRange(level, 0.3f, 0.8f);
    }

    [Fact]
    public void SignalBelowTheFloor_ClampsToZero()
    {
        Assert.Equal(0f, AudioLevelMeter.NormalizeRms(0.0001));
    }

    [Fact]
    public void SignalAboveFullScale_ClampsToOne()
    {
        Assert.Equal(1f, AudioLevelMeter.NormalizeRms(1.5));
    }

    [Fact]
    public void NegativeOrZeroRms_IsZero()
    {
        Assert.Equal(0f, AudioLevelMeter.NormalizeRms(0));
        Assert.Equal(0f, AudioLevelMeter.NormalizeRms(-1));
    }

    [Fact]
    public void EmptyBuffer_IsZero()
    {
        Assert.Equal(0f, AudioLevelMeter.ComputeLevel([], isFloat: true, bitsPerSample: 32));
    }

    // 24-bit and 8-bit devices exist. They must produce a still indicator, not noise read out of
    // misaligned bytes, and above all must not throw on the capture thread.
    [Theory]
    [InlineData(true, 24)]
    [InlineData(false, 24)]
    [InlineData(false, 8)]
    [InlineData(true, 64)]
    public void UnsupportedFormat_IsZeroRatherThanGarbage(bool isFloat, int bitsPerSample)
    {
        byte[] buffer = FloatBuffer([0.5f, -0.5f, 0.5f, -0.5f]);

        Assert.Equal(0f, AudioLevelMeter.ComputeLevel(buffer, isFloat, bitsPerSample));
    }

    // A glitching driver can hand back NaN. Accumulating one poisons the sum for the whole
    // buffer, which would freeze the indicator at zero until the next recording.
    [Fact]
    public void NonFiniteSamples_AreSkippedRatherThanPoisoningTheBuffer()
    {
        byte[] buffer = FloatBuffer([float.NaN, 1f, float.PositiveInfinity, -1f]);

        Assert.Equal(1f, AudioLevelMeter.ComputeLevel(buffer, isFloat: true, bitsPerSample: 32));
    }

    [Fact]
    public void BufferWithATrailingPartialSample_IgnoresTheRemainder()
    {
        byte[] full = FloatBuffer([1f, -1f]);
        byte[] truncated = new byte[full.Length + 3];
        full.CopyTo(truncated, 0);

        Assert.Equal(1f, AudioLevelMeter.ComputeLevel(truncated, isFloat: true, bitsPerSample: 32));
    }

    private static byte[] FloatBuffer(float[] samples)
    {
        byte[] buffer = new byte[samples.Length * 4];
        for (int i = 0; i < samples.Length; i++)
        {
            BitConverter.GetBytes(samples[i]).CopyTo(buffer, i * 4);
        }

        return buffer;
    }

    private static byte[] Pcm16Buffer(short[] samples)
    {
        byte[] buffer = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            BitConverter.GetBytes(samples[i]).CopyTo(buffer, i * 2);
        }

        return buffer;
    }
}
