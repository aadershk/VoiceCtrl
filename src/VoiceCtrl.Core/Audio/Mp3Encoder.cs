using System.IO;
using NAudio.Lame;
using NAudio.Wave;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Audio;

/// <summary>
/// Compresses a dictation clip for upload. The audio itself is 16kHz mono speech that Gemini
/// downsamples internally anyway, so the WAV bytes the recorder produces are roughly ten times
/// larger than they need to be on the wire, and the payload grows by a further third once it is
/// base64'd into the JSON request body. On a long dictation that is most of a second of upload
/// spent carrying nothing the model can use.
/// </summary>
public static class Mp3Encoder
{
    /// <summary>
    /// Enough for intelligible 16kHz mono speech with headroom to spare. Speech recognition is
    /// far more tolerant of compression than music, and the model resamples to a lower rate than
    /// this regardless, so spending more bits buys nothing measurable.
    /// </summary>
    private const int BitRateKbps = 32;

    /// <summary>
    /// Encodes 16-bit PCM WAV bytes to MP3, or returns null if encoding fails for any reason.
    /// Null rather than an exception because this is an optimisation: the caller falls back to
    /// sending the original WAV, which is slower but always works. A missing or unloadable
    /// native libmp3lame on some machine must cost speed, never a transcription.
    /// </summary>
    public static byte[]? TryEncode(byte[] wavBytes)
    {
        try
        {
            using var wavStream = new MemoryStream(wavBytes);
            using var reader = new WaveFileReader(wavStream);
            using var output = new MemoryStream();

            using (var writer = new LameMP3FileWriter(output, reader.WaveFormat, BitRateKbps))
            {
                reader.CopyTo(writer);
            }

            return output.ToArray();
        }
        catch (Exception ex)
        {
            SimpleFileLogger.LogError("Mp3Encode", ex);
            return null;
        }
    }
}
