using VoiceCtrl.Core.Audio;
using Xunit;

namespace VoiceCtrl.Core.Tests.Audio;

public class SpeechConditionerTests
{
    private const int Rate = 16000;

    private static float[] Tone(double seconds, float amplitude) =>
        Enumerable.Range(0, (int)(seconds * Rate)).Select(i => amplitude * (float)Math.Sin(i * 0.1)).ToArray();

    private static float[] Silence(double seconds) => new float[(int)(seconds * Rate)];

    [Fact]
    public void None_ReturnsTheClipUntouched()
    {
        float[] clip = [.. Silence(1), .. Tone(1, 0.3f)];
        Assert.Same(clip, SpeechConditioner.Condition(clip, SpeechConditioning.None));
    }

    [Fact]
    public void Trim_CutsLongSilenceEitherSideAndKeepsPadding()
    {
        float[] clip = [.. Silence(2), .. Tone(1, 0.3f), .. Silence(2)];

        float[] trimmed = SpeechConditioner.Condition(clip, SpeechConditioning.TrimSilence);

        Assert.InRange(trimmed.Length, (int)(1.3 * Rate), (int)(1.7 * Rate));
    }

    [Fact]
    public void Trim_LeavesAClipWithNoSpeechAlone()
    {
        float[] clip = Silence(2);
        Assert.Same(clip, SpeechConditioner.Condition(clip, SpeechConditioning.TrimSilence));
    }

    [Fact]
    public void Normalize_RaisesAQuietClipButNeverBeyondTheGainCap()
    {
        float[] quiet = Tone(1, 0.01f);

        float peak = SpeechConditioner.Condition(quiet, SpeechConditioning.NormalizeGain).Max(Math.Abs);

        Assert.InRange(peak, 0.079f, 0.081f); // 8x cap, not the 0.7 target
    }

    [Fact]
    public void Normalize_NeverTurnsALoudClipDown()
    {
        float[] loud = Tone(1, 0.9f);
        Assert.Same(loud, SpeechConditioner.Condition(loud, SpeechConditioning.NormalizeGain));
    }
}
