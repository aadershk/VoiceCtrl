namespace VoiceCtrl.Core.Audio;

/// <summary>
/// Turns a raw capture buffer into a single 0..1 number for the overlay's level indicator.
/// Separate from the recorder so the sample-format handling can be tested against synthesized
/// buffers: WASAPI hands back whatever the device's shared-mode mix format happens to be, which
/// in practice is 32-bit float on most machines and 16-bit PCM on some, and reading one as the
/// other produces a meter that looks plausible while being completely wrong.
/// </summary>
public static class AudioLevelMeter
{
    /// <summary>Quiet end of the scale. Normal speech sits around -30 to -20 dBFS, so a floor
    /// near the noise level is what makes the indicator move visibly instead of hugging zero.</summary>
    private const double FloorDb = -50.0;

    /// <summary>
    /// Root-mean-square level of <paramref name="buffer"/>, mapped to 0..1 on a decibel scale.
    /// Returns 0 for a format this cannot read, which shows a still indicator rather than a
    /// meaningless one.
    /// </summary>
    public static float ComputeLevel(ReadOnlySpan<byte> buffer, bool isFloat, int bitsPerSample)
    {
        double sumOfSquares = 0;
        int sampleCount = 0;

        if (isFloat && bitsPerSample == 32)
        {
            for (int i = 0; i + 4 <= buffer.Length; i += 4)
            {
                double sample = BitConverter.ToSingle(buffer[i..(i + 4)]);

                // A denormal or NaN from a glitching driver would poison the running sum for the
                // whole buffer, so anything not finite is dropped rather than accumulated.
                if (double.IsFinite(sample))
                {
                    sumOfSquares += sample * sample;
                    sampleCount++;
                }
            }
        }
        else if (!isFloat && bitsPerSample == 16)
        {
            for (int i = 0; i + 2 <= buffer.Length; i += 2)
            {
                double sample = BitConverter.ToInt16(buffer[i..(i + 2)]) / 32768.0;
                sumOfSquares += sample * sample;
                sampleCount++;
            }
        }
        else
        {
            return 0f;
        }

        if (sampleCount == 0)
        {
            return 0f;
        }

        return NormalizeRms(Math.Sqrt(sumOfSquares / sampleCount));
    }

    /// <summary>Maps a linear 0..1 RMS onto 0..1 across <see cref="FloorDb"/> to full scale.
    /// Linear RMS is useless here: speech lives between about 0.01 and 0.1, which on a linear
    /// indicator is the bottom tenth and reads as no movement at all.</summary>
    public static float NormalizeRms(double rms)
    {
        if (rms <= 0)
        {
            return 0f;
        }

        double db = 20.0 * Math.Log10(rms);
        double normalized = (db - FloorDb) / -FloorDb;
        return (float)Math.Clamp(normalized, 0.0, 1.0);
    }
}
