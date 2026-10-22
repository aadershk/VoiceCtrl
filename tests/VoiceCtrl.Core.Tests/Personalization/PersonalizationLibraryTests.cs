using System.IO;
using System.Text;
using VoiceCtrl.Core.Personalization;
using Xunit;

namespace VoiceCtrl.Core.Tests.Personalization;

public class PersonalizationLibraryTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"VoiceCtrlTests_{Path.GetRandomFileName()}");

    private readonly PersonalizationLibrary _library;

    public PersonalizationLibraryTests()
    {
        Directory.CreateDirectory(_directory);
        _library = new PersonalizationLibrary(DictionaryPath, SnippetsPath);
    }

    private string DictionaryPath => Path.Combine(_directory, "dictionary.txt");

    private string SnippetsPath => Path.Combine(_directory, "snippets.txt");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    // ---- Dictionary editing

    [Fact]
    public void SaveWord_AddsOnTopAndKeepsTheFileHeader()
    {
        File.WriteAllText(DictionaryPath, "# my notes\nSchiphol\n");

        Assert.Equal(DictionaryEditError.None, _library.SaveWord(null, " Bluepeak ", "blue peak, blu peak"));

        Assert.Equal("# my notes\nBluepeak | blue peak, blu peak\nSchiphol\n", File.ReadAllText(DictionaryPath));
    }

    [Fact]
    public void SaveWord_EditsInPlace_AndMayChangeTheCase()
    {
        File.WriteAllText(DictionaryPath, "Schiphol\nbluepeak\nGrafana\n");

        Assert.Equal(DictionaryEditError.None, _library.SaveWord("bluepeak", "Bluepeak", ""));

        Assert.Equal(["Schiphol", "Bluepeak", "Grafana"], _library.Dictionary().Select(e => e.Word));
    }

    [Theory]
    [InlineData("  ", "", DictionaryEditError.WordMissing)]
    [InlineData("schiphol", "", DictionaryEditError.AlreadyThere)]
    [InlineData("# not a comment", "", DictionaryEditError.WordBadCharacter)]
    [InlineData("A | B", "", DictionaryEditError.WordBadCharacter)]
    [InlineData("Bluepeak", "blue | peak", DictionaryEditError.HeardAsBadCharacter)]
    [InlineData("Bluepeak", "a, b, c, d, e, f", DictionaryEditError.HeardAsTooMany)]
    public void SaveWord_RefusesWhatTheFileCantHold(string word, string alsoHeardAs, DictionaryEditError expected)
    {
        File.WriteAllText(DictionaryPath, "Schiphol\n");

        Assert.Equal(expected, _library.SaveWord(null, word, alsoHeardAs));
        Assert.Equal("Schiphol\n", File.ReadAllText(DictionaryPath));
    }

    [Fact]
    public void SaveWord_RefusesOverlongWordsAndPhrases()
    {
        string tooLong = new('a', CustomDictionary.MaxTermLength + 1);

        Assert.Equal(DictionaryEditError.WordTooLong, _library.SaveWord(null, tooLong, ""));
        Assert.Equal(DictionaryEditError.HeardAsTooLong, _library.SaveWord(null, "Word", tooLong));
    }

    [Fact]
    public void SaveWord_RefusesANewWordWhenFull()
    {
        File.WriteAllLines(DictionaryPath, Enumerable.Range(0, CustomDictionary.MaxTerms).Select(i => $"term{i}"));

        Assert.Equal(DictionaryEditError.Full, _library.SaveWord(null, "one more", ""));
        Assert.Equal(DictionaryEditError.None, _library.SaveWord("term3", "renamed", ""));
    }

    [Fact]
    public void RemoveThenRestore_PutsTheWordBackWhereItWas()
    {
        File.WriteAllText(DictionaryPath, "A\nBluepeak | blue peak\nC\n");
        DictionaryEntry removed = _library.Dictionary()[1];

        int index = _library.RemoveWord("bluepeak");
        Assert.Equal(["A", "C"], _library.Dictionary().Select(e => e.Word));

        _library.RestoreWord(removed, index);
        Assert.Equal("A\nBluepeak | blue peak\nC\n", File.ReadAllText(DictionaryPath));
    }

    [Fact]
    public void Restore_DoesNothingWhenTheWordIsBackAlready()
    {
        File.WriteAllText(DictionaryPath, "A\n");

        _library.RestoreWord(new DictionaryEntry("a", []), 0);

        Assert.Equal("A\n", File.ReadAllText(DictionaryPath));
    }

    // ---- Snippet editing

    [Fact]
    public void SaveSnippet_RoundTripsLineBreaksAndBackslashes()
    {
        Assert.Equal(SnippetEditError.None, _library.SaveSnippet(null, "folder", "C:\\new folder\r\nnext line"));

        Snippet saved = Assert.Single(_library.Snippets());
        Assert.Equal("C:\\new folder\nnext line", saved.Expansion);
    }

    [Theory]
    [InlineData("", SnippetEditError.TriggerMissing)]
    [InlineData("a = b", SnippetEditError.TriggerBadCharacter)]
    [InlineData("#tag", SnippetEditError.TriggerBadCharacter)]
    [InlineData("BRB", SnippetEditError.AlreadyThere)]
    public void SaveSnippet_RefusesWhatTheFileCantHold(string trigger, SnippetEditError expected)
    {
        File.WriteAllText(SnippetsPath, "brb = be right back\n");

        Assert.Equal(expected, _library.SaveSnippet(null, trigger, "text"));
        Assert.Equal("brb = be right back\n", File.ReadAllText(SnippetsPath));
    }

    [Fact]
    public void SaveSnippet_RefusesAnOverlongExpansion()
    {
        Assert.Equal(
            SnippetEditError.ExpansionTooLong,
            _library.SaveSnippet(null, "long", new string('x', PersonalizationLibrary.MaxExpansionLength + 1)));
    }

    // ---- Import and export

    [Fact]
    public void Dictionary_RoundTripsThroughExportAndImport()
    {
        File.WriteAllText(DictionaryPath, "Schiphol\nBluepeak | blue peak, blu peak\n");
        byte[] exported = _library.ExportDictionary();
        File.WriteAllText(DictionaryPath, string.Empty);

        ImportReport report = _library.ImportDictionary(exported);

        Assert.Equal(new ImportReport(true, 2, 0, 0, []), report with { RejectedLines = [] });
        Assert.Empty(report.RejectedLines);
        Assert.Equal("Schiphol\nBluepeak | blue peak, blu peak\n", File.ReadAllText(DictionaryPath));
    }

    [Fact]
    public void Snippets_RoundTripThroughExportAndImport()
    {
        _library.SaveSnippet(null, "sig", "Kind regards,\nAadersh");
        _library.SaveSnippet(null, "q", "say \"hi\"");
        byte[] exported = _library.ExportSnippets();
        File.Delete(SnippetsPath);

        ImportReport report = _library.ImportSnippets(exported);

        Assert.Equal(2, report.Added);
        Assert.Equal([new Snippet("q", "say \"hi\""), new Snippet("sig", "Kind regards,\nAadersh")], _library.Snippets());
    }

    [Fact]
    public void Import_AddsNewRows_CountsExistingOnes_AndReportsRejectedLines()
    {
        File.WriteAllText(DictionaryPath, "Schiphol\n");
        byte[] file = Utf8("Word,Also heard as\nGrafana,\nschiphol,\n,orphan phrase\nA | B,\nBluepeak,blue peak\ntoo,many,columns\nGrafana,\n");

        ImportReport report = _library.ImportDictionary(file);

        Assert.True(report.Readable);
        Assert.Equal(2, report.Added);
        Assert.Equal(2, report.AlreadyThere);
        Assert.Equal([4, 5, 7], report.RejectedLines);
        Assert.Equal(["Schiphol", "Grafana", "Bluepeak"], _library.Dictionary().Select(e => e.Word));
    }

    [Fact]
    public void Import_StopsAddingAtTheLimit_AndSaysHowManyDidntFit()
    {
        File.WriteAllLines(DictionaryPath, Enumerable.Range(0, CustomDictionary.MaxTerms - 1).Select(i => $"term{i}"));

        ImportReport report = _library.ImportDictionary(Utf8("Word\nnew1\nnew2\nnew3\n"));

        Assert.Equal(1, report.Added);
        Assert.Equal(2, report.NotAddedFull);
        Assert.Equal(CustomDictionary.MaxTerms, _library.Dictionary().Count);
    }

    [Theory]
    [InlineData("Word,Also heard as\nGrafana,\"never closed\n")]
    [InlineData("When you say,VoiceCtrl types\nbrb,be right back\n")]
    [InlineData("not,a,voicectrl,file\n")]
    public void Import_OfABadFile_ChangesNothing(string text)
    {
        File.WriteAllText(DictionaryPath, "# header\nSchiphol\n");

        ImportReport report = _library.ImportDictionary(Utf8(text));

        Assert.Equal(ImportReport.Unreadable, report);
        Assert.Equal("# header\nSchiphol\n", File.ReadAllText(DictionaryPath));
    }

    [Fact]
    public void ImportSnippets_RejectsARowWithoutItsSecondColumn()
    {
        ImportReport report = _library.ImportSnippets(Utf8("When you say,VoiceCtrl types\nbrb\nomw,on my way\n"));

        Assert.Equal(1, report.Added);
        Assert.Equal([2], report.RejectedLines);
    }

    [Fact]
    public void Import_WithNothingNew_LeavesTheFileUntouched()
    {
        File.WriteAllText(DictionaryPath, "Schiphol\n");
        DateTime before = File.GetLastWriteTimeUtc(DictionaryPath);

        ImportReport report = _library.ImportDictionary(Utf8("Word\nSchiphol\n"));

        Assert.Equal(0, report.Added);
        Assert.Equal(1, report.AlreadyThere);
        Assert.Equal(before, File.GetLastWriteTimeUtc(DictionaryPath));
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}
