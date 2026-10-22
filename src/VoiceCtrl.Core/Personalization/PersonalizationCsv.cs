using System.Text;

namespace VoiceCtrl.Core.Personalization;

/// <summary>One CSV record and the file line it starts on (1-based), for the import report.</summary>
public sealed record CsvRecord(int Line, IReadOnlyList<string> Fields);

/// <summary>
/// The Dictionary and Snippets as CSV files (spec §5.3): RFC 4180, UTF-8 with a BOM so Excel
/// reads accents correctly, and a header row that says which list the file holds. Reading is
/// strict about the file's shape and lenient about how a spreadsheet saved it: a semicolon
/// delimiter (Excel in most of Europe), a missing BOM, or an ANSI file all still read.
/// </summary>
public static class PersonalizationCsv
{
    /// <summary>Far above 200 entries of any real length. A bigger file is not one of ours.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    public static IReadOnlyList<string> DictionaryHeader { get; } = ["Word", "Also heard as"];

    public static IReadOnlyList<string> SnippetsHeader { get; } = ["When you say", "VoiceCtrl types"];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static byte[] WriteDictionary(IReadOnlyList<DictionaryEntry> entries) =>
        Write(DictionaryHeader, entries.Select(entry => (IReadOnlyList<string>)[entry.Word, string.Join(", ", entry.AlsoHeardAs)]));

    public static byte[] WriteSnippets(IReadOnlyList<Snippet> snippets) =>
        Write(SnippetsHeader, snippets.Select(snippet => (IReadOnlyList<string>)[snippet.Trigger, snippet.Expansion]));

    /// <summary>The header row, then one row per record, CRLF between records, UTF-8 with a BOM.</summary>
    public static byte[] Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var text = new StringBuilder();
        foreach (IReadOnlyList<string> row in rows.Prepend(header))
        {
            text.AppendJoin(',', row.Select(Quote)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    /// <summary>
    /// The records after the header, or null when the file is not a CSV of this kind: too big,
    /// broken quoting, or a first row that is not <paramref name="header"/>. A header may stop
    /// after its first column (a hand-made one-column list of words), and matches ignoring case.
    /// Blank lines are skipped. Fields are returned as written; judging them is the caller's job.
    /// </summary>
    public static IReadOnlyList<CsvRecord>? Read(byte[] file, IReadOnlyList<string> header)
    {
        if (file.Length > MaxFileBytes)
        {
            return null;
        }

        string text = Decode(file);
        List<CsvRecord>? records = Parse(text, DetectDelimiter(text));
        if (records is null || records.Count == 0 || !IsHeader(records[0].Fields, header))
        {
            return null;
        }

        return records.Skip(1).Where(record => !IsBlank(record.Fields)).ToList();
    }

    private static string Decode(byte[] file)
    {
        ReadOnlySpan<byte> bytes = file;
        ReadOnlySpan<byte> bom = Encoding.UTF8.Preamble;
        if (bytes.StartsWith(bom))
        {
            bytes = bytes[bom.Length..];
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Excel's plain "CSV" saves in the ANSI code page. Latin-1 is the closest one .NET
            // has built in, and it reads every accented letter in Western European names.
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>Semicolon when the first line has one and no comma, as Excel writes it wherever the
    /// decimal separator is a comma; otherwise the RFC's comma.</summary>
    private static char DetectDelimiter(string text)
    {
        int end = text.IndexOfAny(['\r', '\n']);
        ReadOnlySpan<char> firstLine = end < 0 ? text : text.AsSpan(0, end);
        return firstLine.Contains(';') && !firstLine.Contains(',') ? ';' : ',';
    }

    /// <summary>RFC 4180 records. Null on broken quoting: an unclosed quote, or text straight after a
    /// closing one. Accepts CRLF, LF or a lone CR between records.</summary>
    private static List<CsvRecord>? Parse(string text, char delimiter)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        int line = 1;
        int recordLine = 1;
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];
            if (c == '"' && field.Length == 0)
            {
                int? next = ReadQuoted(text, i + 1, field, ref line);
                if (next is null)
                {
                    return null;
                }

                i = next.Value;
                if (i < text.Length && text[i] != delimiter && text[i] != '\r' && text[i] != '\n')
                {
                    return null;
                }

                continue;
            }

            if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                i++;
            }
            else if (c is '\r' or '\n')
            {
                fields.Add(field.ToString());
                field.Clear();
                records.Add(new CsvRecord(recordLine, fields.ToArray()));
                fields.Clear();
                i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                line++;
                recordLine = line;
            }
            else
            {
                field.Append(c);
                i++;
            }
        }

        // A last record without a line break after it.
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(new CsvRecord(recordLine, fields.ToArray()));
        }

        return records;
    }

    /// <summary>Reads a quoted field's content from just after its opening quote. Returns the index
    /// just after the closing quote, or null if the quote is never closed.</summary>
    private static int? ReadQuoted(string text, int start, StringBuilder field, ref int line)
    {
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                    continue;
                }

                return i + 1;
            }

            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                // A line break inside a field is kept as \n, the app's own line break.
                field.Append('\n');
                i++;
                line++;
                continue;
            }

            if (c is '\r' or '\n')
            {
                line++;
                c = '\n';
            }

            field.Append(c);
        }

        return null;
    }

    private static bool IsHeader(IReadOnlyList<string> fields, IReadOnlyList<string> header)
    {
        if (fields.Count == 0 || fields.Count > header.Count)
        {
            return false;
        }

        for (int i = 0; i < fields.Count; i++)
        {
            if (!string.Equals(fields[i].Trim(), header[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBlank(IReadOnlyList<string> fields) => fields.All(string.IsNullOrWhiteSpace);

    /// <summary>Quotes a field only when it has to be: it holds a comma, semicolon, quote or line
    /// break, or starts or ends with a space a spreadsheet would otherwise trim.</summary>
    private static string Quote(string field)
    {
        bool needsQuotes = field.AsSpan().IndexOfAny(",;\"\r\n") >= 0
            || (field.Length > 0 && (char.IsWhiteSpace(field[0]) || char.IsWhiteSpace(field[^1])));

        return needsQuotes ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
    }
}
