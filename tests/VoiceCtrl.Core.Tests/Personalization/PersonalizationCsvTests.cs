using System.Text;
using VoiceCtrl.Core.Personalization;
using Xunit;

namespace VoiceCtrl.Core.Tests.Personalization;

public class PersonalizationCsvTests
{
    private static readonly IReadOnlyList<string> Header = PersonalizationCsv.SnippetsHeader;

    [Fact]
    public void Write_HasABomAHeaderAndCrlfRecords()
    {
        byte[] file = PersonalizationCsv.WriteDictionary([new DictionaryEntry("Schiphol", [])]);

        Assert.Equal(Encoding.UTF8.GetPreamble(), file[..3]);
        Assert.Equal("Word,Also heard as\r\nSchiphol,\r\n", Encoding.UTF8.GetString(file[3..]));
    }

    [Fact]
    public void Write_QuotesOnlyWhatNeedsIt()
    {
        byte[] file = PersonalizationCsv.WriteSnippets([
            new Snippet("sig", "Kind regards,\nAadersh"),
            new Snippet("quote", "say \"hi\""),
            new Snippet("plain", "no quotes here"),
            new Snippet("pad", " leading space"),
        ]);

        string text = Encoding.UTF8.GetString(file[3..]);

        Assert.Contains("sig,\"Kind regards,\nAadersh\"\r\n", text, StringComparison.Ordinal);
        Assert.Contains("quote,\"say \"\"hi\"\"\"\r\n", text, StringComparison.Ordinal);
        Assert.Contains("plain,no quotes here\r\n", text, StringComparison.Ordinal);
        Assert.Contains("pad,\" leading space\"\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrips_EveryAwkwardCharacter()
    {
        Snippet[] snippets = [
            new("my address", "Keizersgracht 1\n1015 CJ Amsterdam"),
            new("q", "\"quoted\", with; separators"),
            new("café", "Crème brûlée, naïve, Zoë, Ødegaard"),
            new("empty", ""),
        ];

        IReadOnlyList<CsvRecord>? records = PersonalizationCsv.Read(PersonalizationCsv.WriteSnippets(snippets), Header);

        Assert.NotNull(records);
        Assert.Equal(snippets, records.Select(r => new Snippet(r.Fields[0], r.Fields[1])));
    }

    [Fact]
    public void Read_ReportsTheLineEachRecordStartsOn()
    {
        byte[] file = Utf8("When you say,VoiceCtrl types\r\na,\"two\r\nlines\"\r\n\r\nb,c\r\n");

        IReadOnlyList<CsvRecord>? records = PersonalizationCsv.Read(file, Header);

        Assert.NotNull(records);
        Assert.Equal([2, 5], records.Select(r => r.Line));
        Assert.Equal("two\nlines", records[0].Fields[1]);
    }

    [Theory]
    [InlineData("When you say,VoiceCtrl types\na,\"never closed\n")]
    [InlineData("When you say,VoiceCtrl types\na,\"closed\"then text\n")]
    [InlineData("Word,Also heard as\nSchiphol,\n")]
    [InlineData("")]
    [InlineData("just some notes\nnot a csv at all\n")]
    public void Read_RejectsAFileThatIsNotOneOfOurs(string text)
    {
        Assert.Null(PersonalizationCsv.Read(Utf8(text), Header));
    }

    [Fact]
    public void Read_RejectsAFileOverTheSizeLimit()
    {
        byte[] file = Utf8("When you say,VoiceCtrl types\n" + new string('a', PersonalizationCsv.MaxFileBytes));

        Assert.Null(PersonalizationCsv.Read(file, Header));
    }

    [Fact]
    public void Read_AcceptsWhatExcelSaves_SemicolonsAndNoBom()
    {
        IReadOnlyList<CsvRecord>? records = PersonalizationCsv.Read(Utf8("when you say;voicectrl types\nbrb;be right back"), Header);

        Assert.NotNull(records);
        Assert.Equal(["brb", "be right back"], Assert.Single(records).Fields);
    }

    [Fact]
    public void Read_FallsBackToLatin1ForAnAnsiFile()
    {
        byte[] file = Encoding.Latin1.GetBytes("Word\nZoë\n");

        IReadOnlyList<CsvRecord>? records = PersonalizationCsv.Read(file, PersonalizationCsv.DictionaryHeader);

        Assert.NotNull(records);
        Assert.Equal("Zoë", Assert.Single(records).Fields[0]);
    }

    [Fact]
    public void Read_AcceptsAOneColumnHeader()
    {
        IReadOnlyList<CsvRecord>? records = PersonalizationCsv.Read(Utf8("Word\nSchiphol\nGrafana\n"), PersonalizationCsv.DictionaryHeader);

        Assert.NotNull(records);
        Assert.Equal(2, records.Count);
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}
