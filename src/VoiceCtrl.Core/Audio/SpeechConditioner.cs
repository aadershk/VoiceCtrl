namespace VoiceCtrl.Core.Audio;

/// <summary>Which clean-ups run on a clip before the offline recogniser sees it.</summary>
[Flags]
public enum SpeechConditioning
{
    None = 0,
    /// <summary>Cut the silence (and key-press thumps) before the first and after the last word.</summary>
    TrimSilence = 1,
    /// <summary>Scale a quiet clip up toward a fixed peak.</summary>
    NormalizeGain = 2,
}

/// <summary>
/// Prepares a push-to-talk clip for the offline recogniser. The whole press-to-release recording
/// goes in, including the beat of silence either side; recognisers decode that silence and can
/// invent words from it, and a laptop mic often records quietly.
/// </summary>
public static class SpeechConditioner
{
    private const int SampleRate = 16000;
    private const int FrameSamples = SampleRate / 50; // 20 ms
    private const float PeakTarget = 0.7f;            // about -3 dBFS
    private const float MaxGain = 8f;                 // about +18 dB: never turn hiss into speech
    private static readonly int PaddingFrames = 12;   // 240 ms of room kept around speech

    public static float[] Condition(float[] samples, SpeechConditioning conditioning)
    {
        if (conditioning == SpeechConditioning.None || samples.Length < FrameSamples * 4)
        {
            return samples;
        }

        float[] result = samples;
        if (conditioning.HasFlag(SpeechConditioning.TrimSilence))
        {
            result = Trim(result);
        }

        if (conditioning.HasFlag(SpeechConditioning.NormalizeGain))
        {
            result = Normalize(result);
        }

        return result;
    }

    /// <summary>
    /// Keeps the span from the first to the last frame that is clearly louder than the clip's own
    /// background, plus padding. The bar is relative (a multiple of the quietest tenth of the
    /// frames), so a noisy room and a quiet one are judged by their own floor; with a floor of
    /// near-digital-silence a small absolute minimum applies instead.
    /// </summary>
    private static float[] Trim(float[] samples)
    {
        int frameCount = samples.Length / FrameSamples;
        float[] energy = new float[frameCount];
        for (int frame = 0; frame < frameCount; frame++)
        {
            double sum = 0;
            for (int i = frame * FrameSamples; i < (frame + 1) * FrameSamples; i++)
            {
                sum += samples[i] * samples[i];
            }

            energy[frame] = (float)Math.Sqrt(sum / FrameSamples);
        }

        float[] sorted = (float[])energy.Clone();
        Array.Sort(sorted);
        float floor = sorted[sorted.Length / 10];
        float threshold = Math.Max(floor * 4f, 0.004f);

        int first = Array.FindIndex(energy, e => e > threshold);
        int last = Array.FindLastIndex(energy, e => e > threshold);
        if (first < 0)
        {
            return samples; // nothing clearly louder than the floor: leave the clip alone
        }

        int from = Math.Max(0, first - PaddingFrames) * FrameSamples;
        int to = Math.Min(frameCount, last + 1 + PaddingFrames) * FrameSamples;
        return samples[from..to];
    }

    private static float[] Normalize(float[] samples)
    {
        float peak = 0;
        foreach (float sample in samples)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        if (peak < 1e-4f)
        {
            return samples;
        }

        float gain = Math.Min(PeakTarget / peak, MaxGain);
        if (gain <= 1.05f)
        {
            return samples; // already loud enough; never turn a loud clip down
        }

        float[] scaled = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            scaled[i] = samples[i] * gain;
        }

        return scaled;
    }
}
