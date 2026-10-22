using System.IO;
using System.Text;
using VoiceCtrl.Core.Config;

namespace VoiceCtrl.Core.Personalization;

/// <summary>One Snippet: say <see cref="Trigger"/>, get <see cref="Expansion"/>.</summary>
public sealed record Snippet(string Trigger, string Expansion);

/// <summary>Why a Dictionary word wasn't saved. The Hub shows each under the field it names.</summary>
public enum DictionaryEditError
{
    None,
    WordMissing,
    WordTooLong,
    /// <summary>A bar (the file's separator), a line break, or a leading # (a comment line).</summary>
    WordBadCharacter,
    AlreadyThere,
    Full,
    HeardAsTooLong,
    HeardAsTooMany,
    HeardAsBadCharacter,
}

/// <summary>Why a Snippet wasn't saved. The Hub shows each under the field it names.</summary>
public enum SnippetEditError
{
    None,
    TriggerMissing,
    TriggerTooLong,
    /// <summary>An equals sign (the file's separator), a line break, or a leading # (a comment line).</summary>
    TriggerBadCharacter,
    AlreadyThere,
    Full,
    ExpansionTooLong,
}

/// <summary>
/// What an import did. <see cref="Readable"/> false means the file wasn't a VoiceCtrl CSV of this
/// kind and nothing changed. Otherwise every usable new row was added in one write:
/// <see cref="RejectedLines"/> are rows that couldn't be used, with their file line numbers.
/// </summary>
public sealed record ImportReport(bool Readable, int Added, int AlreadyThere, int NotAddedFull, IReadOnlyList<int> RejectedLines)
{
    public static ImportReport Unreadable { get; } = new(false, 0, 0, 0, []);
}

/// <summary>
/// The Hub's side of the Dictionary and Snippets (spec §5.3): reads, edits, imports and exports
/// <c>dictionary.txt</c> and <c>snippets.txt</c>. Every change rereads the file first, so an edit
/// made in Notepad meanwhile is kept, and writes it whole and atomically, keeping the file's own
/// comment header. Transcription picks the change up on its next read (<see cref="UserFileCache{T}"/>).
/// The rules for what can be saved live here, once, for the editor and the import alike.
/// </summary>
public sealed class PersonalizationLibrary(string dictionaryPath, string snippetsPath)
{
    public const int MaxTriggerLength = 64;

    public const int MaxExpansionLength = 2000;

    public static PersonalizationLibrary CreateDefault() => new(UserDataPaths.Dictionary, UserDataPaths.Snippets);

    public IReadOnlyList<DictionaryEntry> Dictionary() => CustomDictionary.Parse(SplitLines(ReadText(dictionaryPath)));

    public IReadOnlyList<Snippet> Snippets() =>
        SnippetTable.Parse(SplitLines(ReadText(snippetsPath))).Snippets.Select(s => new Snippet(s.Key, s.Value)).ToList();

    // ---- Dictionary

    /// <summary>
    /// Adds a word (<paramref name="original"/> null) or replaces the word named
    /// <paramref name="original"/> in place. <paramref name="alsoHeardAs"/> is the field's text,
    /// phrases separated by commas. An original that is gone (removed in Notepad) adds instead.
    /// </summary>
    public DictionaryEditError SaveWord(string? original, string word, string alsoHeardAs)
    {
        DictionaryEditError error = ValidateWord(word, alsoHeardAs, out DictionaryEntry entry);
        if (error != DictionaryEditError.None)
        {
            return error;
        }

        List<DictionaryEntry> entries = [.. Dictionary()];
        int at = original is null ? -1 : entries.FindIndex(e => SameWord(e.Word, original));
        if (entries.Where((e, i) => i != at).Any(e => SameWord(e.Word, entry.Word)))
        {
            return DictionaryEditError.AlreadyThere;
        }

        if (at < 0)
        {
            if (entries.Count >= CustomDictionary.MaxTerms)
            {
                return DictionaryEditError.Full;
            }

            // New words go on top, where the Hub shows them.
            entries.Insert(0, entry);
        }
        else
        {
            entries[at] = entry;
        }

        WriteDictionary(entries);
        return DictionaryEditError.None;
    }

    /// <summary>Removes the word and returns where it was, for Undo; -1 if it wasn't there.</summary>
    public int RemoveWord(string word)
    {
        List<DictionaryEntry> entries = [.. Dictionary()];
        int at = entries.FindIndex(e => SameWord(e.Word, word));
        if (at >= 0)
        {
            entries.RemoveAt(at);
            WriteDictionary(entries);
        }

        return at;
    }

    /// <summary>Undo for <see cref="RemoveWord"/>: puts the word back where it was. Nothing happens
    /// if it is back already, the list is full, or the entry isn't one the editor would save.</summary>
    public void RestoreWord(DictionaryEntry removed, int index)
    {
        if (ValidateWord(removed.Word, string.Join(", ", removed.AlsoHeardAs), out DictionaryEntry entry) != DictionaryEditError.None)
        {
            return;
        }

        List<DictionaryEntry> entries = [.. Dictionary()];
        if (entries.Count >= CustomDictionary.MaxTerms || entries.Any(e => SameWord(e.Word, entry.Word)))
        {
            return;
        }

        entries.Insert(Math.Clamp(index, 0, entries.Count), entry);
        WriteDictionary(entries);
    }

    /// <summary>Adds the file's new words below the existing ones. Words already there are left as
    /// they are, never overwritten.</summary>
    public ImportReport ImportDictionary(byte[] file)
    {
        IReadOnlyList<CsvRecord>? records = PersonalizationCsv.Read(file, PersonalizationCsv.DictionaryHeader);
        if (records is null)
        {
            return ImportReport.Unreadable;
        }

        List<DictionaryEntry> entries = [.. Dictionary()];
        var tally = new ImportTally();
        foreach (CsvRecord record in records)
        {
            if (record.Fields.Count > 2
                || ValidateWord(record.Fields[0], record.Fields.Count > 1 ? record.Fields[1] : string.Empty, out DictionaryEntry entry) != DictionaryEditError.None)
            {
                tally.Rejected.Add(record.Line);
            }
            else if (entries.Any(e => SameWord(e.Word, entry.Word)))
            {
                tally.AlreadyThere++;
            }
            else if (entries.Count >= CustomDictionary.MaxTerms)
            {
                tally.NotAddedFull++;
            }
            else
            {
                entries.Add(entry);
                tally.Added++;
            }
        }

        if (tally.Added > 0)
        {
            WriteDictionary(entries);
        }

        return tally.Report();
    }

    public byte[] ExportDictionary() => PersonalizationCsv.WriteDictionary(Dictionary());

    // ---- Snippets

    /// <summary>Adds a Snippet (<paramref name="original"/> null) or replaces the one whose trigger
    /// is <paramref name="original"/>, in place.</summary>
    public SnippetEditError SaveSnippet(string? original, string trigger, string expansion)
    {
        SnippetEditError error = ValidateSnippet(trigger, expansion, out Snippet snippet);
        if (error != SnippetEditError.None)
        {
            return error;
        }

        List<Snippet> snippets = [.. Snippets()];
        int at = original is null ? -1 : snippets.FindIndex(s => SameWord(s.Trigger, original));
        if (snippets.Where((s, i) => i != at).Any(s => SameWord(s.Trigger, snippet.Trigger)))
        {
            return SnippetEditError.AlreadyThere;
        }

        if (at < 0)
        {
            if (snippets.Count >= SnippetTable.MaxSnippets)
            {
                return SnippetEditError.Full;
            }

            snippets.Insert(0, snippet);
        }
        else
        {
            snippets[at] = snippet;
        }

        WriteSnippets(snippets);
        return SnippetEditError.None;
    }

    public int RemoveSnippet(string trigger)
    {
        List<Snippet> snippets = [.. Snippets()];
        int at = snippets.FindIndex(s => SameWord(s.Trigger, trigger));
        if (at >= 0)
        {
            snippets.RemoveAt(at);
            WriteSnippets(snippets);
        }

        return at;
    }

    public void RestoreSnippet(Snippet removed, int index)
    {
        if (ValidateSnippet(removed.Trigger, removed.Expansion, out Snippet snippet) != SnippetEditError.None)
        {
            return;
        }

        List<Snippet> snippets = [.. Snippets()];
        if (snippets.Count >= SnippetTable.MaxSnippets || snippets.Any(s => SameWord(s.Trigger, snippet.Trigger)))
        {
            return;
        }

        snippets.Insert(Math.Clamp(index, 0, snippets.Count), snippet);
        WriteSnippets(snippets);
    }

    public ImportReport ImportSnippets(byte[] file)
    {
        IReadOnlyList<CsvRecord>? records = PersonalizationCsv.Read(file, PersonalizationCsv.SnippetsHeader);
        if (records is null)
        {
            return ImportReport.Unreadable;
        }

        List<Snippet> snippets = [.. Snippets()];
        var tally = new ImportTally();
        foreach (CsvRecord record in records)
        {
            // Both columns, always: a trigger with no expansion is a row that lost its second cell.
            if (record.Fields.Count != 2 || ValidateSnippet(record.Fields[0], record.Fields[1], out Snippet snippet) != SnippetEditError.None)
            {
                tally.Rejected.Add(record.Line);
            }
            else if (snippets.Any(s => SameWord(s.Trigger, snippet.Trigger)))
            {
                tally.AlreadyThere++;
            }
            else if (snippets.Count >= SnippetTable.MaxSnippets)
            {
                tally.NotAddedFull++;
            }
            else
            {
                snippets.Add(snippet);
                tally.Added++;
            }
        }

        if (tally.Added > 0)
        {
            WriteSnippets(snippets);
        }

        return tally.Report();
    }

    public byte[] ExportSnippets() => PersonalizationCsv.WriteSnippets(Snippets());

    // ---- Rules

    public static DictionaryEditError ValidateWord(string word, string alsoHeardAs, out DictionaryEntry entry)
    {
        string trimmed = word.Trim();
        entry = new DictionaryEntry(trimmed, []);

        if (trimmed.Length == 0)
        {
            return DictionaryEditError.WordMissing;
        }

        if (trimmed.Length > CustomDictionary.MaxTermLength)
        {
            return DictionaryEditError.WordTooLong;
        }

        if (trimmed.StartsWith('#') || trimmed.AsSpan().IndexOfAny(['|', '\r', '\n']) >= 0)
        {
            return DictionaryEditError.WordBadCharacter;
        }

        string[] phrases = [.. alsoHeardAs.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0)];
        if (phrases.Any(p => p.AsSpan().IndexOfAny(['|', '\r', '\n']) >= 0))
        {
            return DictionaryEditError.HeardAsBadCharacter;
        }

        if (phrases.Any(p => p.Length > CustomDictionary.MaxTermLength))
        {
            return DictionaryEditError.HeardAsTooLong;
        }

        if (phrases.Length > CustomDictionary.MaxAlsoHeardAs)
        {
            return DictionaryEditError.HeardAsTooMany;
        }

        entry = new DictionaryEntry(trimmed, CustomDictionary.ParseAlsoHeardAs(trimmed, alsoHeardAs));
        return DictionaryEditError.None;
    }

    public static SnippetEditError ValidateSnippet(string trigger, string expansion, out Snippet snippet)
    {
        string trimmedTrigger = trigger.Trim();
        // The file trims an expansion when it reads it, so saving it trimmed keeps what the Hub
        // shows and what gets typed the same.
        string trimmedExpansion = expansion.ReplaceLineEndings("\n").Trim();
        snippet = new Snippet(trimmedTrigger, trimmedExpansion);

        if (trimmedTrigger.Length == 0)
        {
            return SnippetEditError.TriggerMissing;
        }

        if (trimmedTrigger.Length > MaxTriggerLength)
        {
            return SnippetEditError.TriggerTooLong;
        }

        if (trimmedTrigger.StartsWith('#') || trimmedTrigger.AsSpan().IndexOfAny(['=', '\r', '\n']) >= 0)
        {
            return SnippetEditError.TriggerBadCharacter;
        }

        return trimmedExpansion.Length > MaxExpansionLength ? SnippetEditError.ExpansionTooLong : SnippetEditError.None;
    }

    private static bool SameWord(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void WriteDictionary(IReadOnlyList<DictionaryEntry> entries)
    {
        string header = ReadHeader(dictionaryPath, CustomDictionary.ExtractHeader);
        AtomicFile.WriteAllBytes(dictionaryPath, Encoding.UTF8.GetBytes(CustomDictionary.Format(header, entries)));
    }

    private void WriteSnippets(IReadOnlyList<Snippet> snippets)
    {
        string header = ReadHeader(snippetsPath, SnippetTable.ExtractHeader);
        string contents = SnippetTable.Format(header, snippets.Select(s => new KeyValuePair<string, string>(s.Trigger, s.Expansion)).ToList());
        AtomicFile.WriteAllBytes(snippetsPath, Encoding.UTF8.GetBytes(contents));
    }

    /// <summary>The file's comment header, or nothing for a missing or empty file (which would
    /// otherwise read as one blank header line).</summary>
    private static string ReadHeader(string path, Func<string, string> extract)
    {
        string text = ReadText(path);
        return text.Length == 0 ? string.Empty : extract(text);
    }

    private static string ReadText(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    private static string[] SplitLines(string contents) => contents.Split('\n');

    private sealed class ImportTally
    {
        public int Added { get; set; }

        public int AlreadyThere { get; set; }

        public int NotAddedFull { get; set; }

        public List<int> Rejected { get; } = [];

        public ImportReport Report() => new(true, Added, AlreadyThere, NotAddedFull, Rejected);
    }
}
