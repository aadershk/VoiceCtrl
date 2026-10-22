using System.IO;
using NAudio.Utils;
using NAudio.Wave;

namespace VoiceCtrl.Core.Online;

/// <summary>
/// Cuts a long recording into pieces a chat completions provider takes in one request, at its
/// quietest moments, so no word is cut in two (ticket 60). Gemini has a Files API for a 20-minute
/// Dictation; the OpenAI-compatible providers have none, and NVIDIA's omni model refused a
/// 10-minute WAV inline ("Failed to load audio", 2026-10-21) while it took 6 minutes in 24 s.
/// </summary>
public static class AudioChunks
{
    /// <summary>Well under the 6 minutes measured to work, so a slower speaker's clip is no larger.</summary>
    public static readonly TimeSpan MaxChunk = TimeSpan.FromMinutes(4);

    /// <summary>A cut is looked for in the last fifth of each piece.</summary>
    private const double SearchFrom = 0.8;

    private const int FrameMs = 20;

    /// <summary>A pause this long, the quietest in the search window, is where a piece ends.</summary>
    private const int PauseFrames = 15;

    /// <summary>The clip in pieces of at most <paramref name="maxChunk"/>; the clip itself when it
    /// is short enough, or not a 16-bit PCM WAV this can read.</summary>
    public static IReadOnlyList<byte[]> Split(byte[] wav, TimeSpan? maxChunk = null)
    {
        TimeSpan max = maxChunk ?? MaxChunk;
        short[] samples;
        WaveFormat format;
        try
        {
            using var reader = new WaveFileReader(new MemoryStream(wav));
            format = reader.WaveFormat;
            if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16 || format.Channels != 1 ||
                reader.TotalTime <= max)
            {
                return [wav];
            }

            byte[] pcm = new byte[reader.Length];
            int read = reader.Read(pcm, 0, pcm.Length);
            samples = new short[read / 2];
            Buffer.BlockCopy(pcm, 0, samples, 0, samples.Length * 2);
        }
        catch (FormatException)
        {
            return [wav];
        }

        int rate = format.SampleRate;
        int frame = rate * FrameMs / 1000;
        int maxSamples = (int)(max.TotalSeconds * rate);
        List<byte[]> pieces = [];
        int start = 0;
        while (samples.Length - start > maxSamples)
        {
            int end = QuietestCut(samples, start + (int)(maxSamples * SearchFrom), start + maxSamples, frame);
            pieces.Add(ToWav(samples, start, end - start, format));
            start = end;
        }

        pieces.Add(ToWav(samples, start, samples.Length - start, format));
        return pieces;
    }

    /// <summary>The middle of the quietest <see cref="PauseFrames"/>-frame stretch between
    /// <paramref name="from"/> and <paramref name="to"/>.</summary>
    private static int QuietestCut(short[] samples, int from, int to, int frame)
    {
        int frames = Math.Max(1, (to - from) / frame);
        double[] energy = new double[frames];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            int offset = from + f * frame;
            for (int i = offset; i < offset + frame && i < samples.Length; i++)
            {
                sum += (double)samples[i] * samples[i];
            }

            energy[f] = sum;
        }

        int window = Math.Min(PauseFrames, frames);
        double best = double.MaxValue;
        int bestStart = 0;
        double running = energy.Take(window).Sum();
        for (int f = 0; ; f++)
        {
            if (running < best)
            {
                best = running;
                bestStart = f;
            }

            if (f + window >= frames)
            {
                break;
            }

            running += energy[f + window] - energy[f];
        }

        return from + (bestStart + window / 2) * frame;
    }

    private static byte[] ToWav(short[] samples, int start, int count, WaveFormat format)
    {
        using var stream = new MemoryStream();
        using (var writer = new WaveFileWriter(new IgnoreDisposeStream(stream), format))
        {
            byte[] bytes = new byte[count * 2];
            Buffer.BlockCopy(samples, start * 2, bytes, 0, bytes.Length);
            writer.Write(bytes, 0, bytes.Length);
        }

        return stream.ToArray();
    }
}
