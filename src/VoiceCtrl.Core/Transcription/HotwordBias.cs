using System.IO;
using System.Linq;
using VoiceCtrl.Core.Personalization;

namespace VoiceCtrl.Core.Transcription;

/// <summary>
/// Decode-time biasing of the English Parakeet model toward the Dictionary's words (ticket 36c).
/// sherpa-onnx raises the score of token paths that spell a listed word, so a name the model heard
/// almost right comes out right. It only runs when the Dictionary has words, because beam search is a
/// little slower and has an upstream bug (sherpa-onnx issue 3267: empty or "Yeah." output) that the
/// client guards against by decoding again with greedy search.
/// </summary>
/// <remarks>
/// Measured on the 40-script set (results in .scratch/v4-ship/accuracy/slice-36c-results.md): English
/// 81.4% to 86.4% of names right with the corrector on top, WER 9.8% to 9.5%, 0 empty outputs in 600
/// decodes. Score 1.5 is the middle of a plateau (1.0 and 2.0 are within 3 points); 3.0 hurts WER.
/// Parakeet v3 (84.3%) and Whisper did not clear the bar and stay greedy.
/// </remarks>
internal sealed record HotwordBias(string HotwordsFile, string BpeVocabFile)
{
    public const float Score = 1.5f;

    /// <summary>Beam search cost grows with the list; a Dictionary beyond this is a Dictionary nobody speaks all of.</summary>
    private const int MaxTerms = 200;

    /// <summary>The words to bias toward for <paramref name="model"/>: empty (no bias) for any model but the English one.</summary>
    public static IReadOnlyList<string> TermsFor(OfflineModel model, IReadOnlyList<DictionaryEntry> dictionary)
    {
        if (model.Repo != OfflineModels.English.Repo)
        {
            return [];
        }

        return dictionary
            .Select(entry => entry.Word.Trim())
            .Where(word => word.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTerms)
            .ToList();
    }

    /// <summary>Writes the hotwords file and the BPE vocabulary sherpa wants, beside the model's own files.</summary>
    public static HotwordBias Write(string modelDirectory, IReadOnlyList<string> terms)
    {
        Directory.CreateDirectory(modelDirectory);
        string hotwords = Path.Combine(modelDirectory, "hotwords.txt");
        File.WriteAllLines(hotwords, terms);

        // sherpa reads "piece<TAB>score"; the model's tokens.txt is "piece id", so the id stands in as a negated score.
        string vocabulary = Path.Combine(modelDirectory, "bpe.vocab");
        File.WriteAllLines(vocabulary, File.ReadLines(Path.Combine(modelDirectory, "tokens.txt"))
            .Select(line => line.LastIndexOf(' ') is var split and > 0 && int.TryParse(line[(split + 1)..], out int id)
                ? $"{line[..split]}\t{-id}"
                : null)
            .OfType<string>());
        return new HotwordBias(hotwords, vocabulary);
    }

    /// <summary>The upstream bug's signature: nothing, or the stray "Yeah." it hallucinates.</summary>
    public static bool LooksLikeBeamFailure(string text)
    {
        string trimmed = text.Trim().TrimEnd('.', '!', '?', ' ');
        return trimmed.Length == 0 || trimmed.Equals("Yeah", StringComparison.OrdinalIgnoreCase);
    }
}
