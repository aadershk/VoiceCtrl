using System.Text;

namespace VoiceCtrl.Core.Personalization;

/// <summary>
/// A rough "how does this sound" key for English-ish words, so a recogniser's spelling of a name
/// ("Skippel", "Phebe", "kuber netties") can be compared with the speaker's spelling ("Schiphol",
/// "Phoebe", "Kubernetes") even when the two are far apart letter by letter.
///
/// It is deliberately a skeleton rather than a pronunciation: vowels are dropped (recognisers get
/// them wrong most often and they carry the least identity), and consonants that sound alike or
/// differ only by voicing (p/b, t/d, k/g, f/v, s/z) share one class, since those are the
/// confusions a recogniser makes on unfamiliar words. Two words with the same key are candidates,
/// not matches; the corrector adds its own guards before it rewrites anything.
/// </summary>
public static class PhoneticKey
{
    /// <summary>The key for <paramref name="text"/>; spaces, punctuation and digits are ignored, so a
    /// phrase and the same phrase run together give the same key.</summary>
    public static string Encode(string text)
    {
        var letters = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                letters.Append(char.ToLowerInvariant(RemoveAccent(c)));
            }
        }

        string word = Respell(letters.ToString());
        var key = new StringBuilder(word.Length);

        for (int index = 0; index < word.Length; index++)
        {
            char c = word[index];
            char? sound = c switch
            {
                'p' or 'b' => 'P',
                't' or 'd' => 'T',
                'k' or 'g' or 'q' => 'K',
                'f' or 'v' => 'F',
                's' or 'z' => 'S',
                'm' => 'M',
                'n' => 'N',
                'l' => 'L',
                'r' => 'R',
                'j' => 'J',
                'X' => 'X', // produced by Respell for "sh"/"ch"
                // A vowel only counts when it opens the word: "Aadersh" and "Adersh" start alike
                // either way, but a word that starts with a consonant should not gain a letter.
                'a' or 'e' or 'i' or 'o' or 'u' or 'y' => index == 0 ? 'A' : null,
                // h, w and the rest are too weak or too unreliable in the recogniser's output.
                _ => null,
            };

            if (sound is { } value && (key.Length == 0 || key[^1] != value))
            {
                key.Append(value);
            }
        }

        return key.ToString();
    }

    /// <summary>
    /// How far apart two keys are, as a plain edit distance. Zero means the words are
    /// indistinguishable at this resolution.
    /// </summary>
    public static int Distance(string keyA, string keyB)
    {
        int[] previous = new int[keyB.Length + 1];
        int[] current = new int[keyB.Length + 1];

        for (int column = 0; column <= keyB.Length; column++)
        {
            previous[column] = column;
        }

        for (int row = 1; row <= keyA.Length; row++)
        {
            current[0] = row;
            for (int column = 1; column <= keyB.Length; column++)
            {
                int substitution = keyA[row - 1] == keyB[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[keyB.Length];
    }

    /// <summary>Spelling patterns whose sound is not the sound of their letters, rewritten to
    /// the letters that would make it. Order matters: longer patterns first.</summary>
    private static string Respell(string word)
    {
        // The pairs are (spelling, sound). Applied in order, left to right, over the whole word.
        (string From, string To)[] rules =
        [
            ("tion", "Xn"), ("sch", "sk"), ("tch", "X"), ("sh", "X"), ("ch", "X"), ("ph", "f"),
            ("ck", "k"), ("kn", "n"), ("wr", "r"), ("gn", "n"), ("mb", "m"), ("wh", "w"),
            ("ps", "s"), ("x", "ks"), ("c", "k"),
        ];

        // "c" is soft before e, i and y (Cecil, city), and that must be decided before the
        // blanket c-to-k rule runs.
        var softened = new StringBuilder(word.Length);
        for (int index = 0; index < word.Length; index++)
        {
            bool soft = word[index] == 'c' && index + 1 < word.Length && word[index + 1] is 'e' or 'i' or 'y';
            softened.Append(soft ? 's' : word[index]);
        }

        string result = softened.ToString();
        foreach ((string from, string to) in rules)
        {
            result = result.Replace(from, to, StringComparison.Ordinal);
        }

        return result;
    }

    private static char RemoveAccent(char c)
    {
        string decomposed = c.ToString().Normalize(NormalizationForm.FormD);
        return decomposed.Length > 0 && char.IsLetter(decomposed[0]) ? decomposed[0] : c;
    }
}
