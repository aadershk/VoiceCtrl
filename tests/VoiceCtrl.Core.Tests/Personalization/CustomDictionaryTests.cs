using VoiceCtrl.Core.Personalization;
using Xunit;

namespace VoiceCtrl.Core.Tests.Personalization;

public class CustomDictionaryTests
{
    [Fact]
    public void ReadsOneTermPerLine()
    {
        IReadOnlyList<string> terms = Words(["Schiphol", "Grafana"]);
        Assert.Equal(new[] { "Schiphol", "Grafana" }, terms.ToArray());
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        IReadOnlyList<string> terms = Words(["  Schiphol\t"]);
        Assert.Equal("Schiphol", Assert.Single(terms));
    }

    [Fact]
    public void SkipsBlankAndCommentLines()
    {
        IReadOnlyList<string> terms = Words(["# a comment", "", "   ", "Schiphol"]);
        Assert.Equal("Schiphol", Assert.Single(terms));
    }

    [Fact]
    public void SeedFileParsesToNoTerms()
    {
        // The file shipped on first run is entirely commented, so a user who never edits it gets
        // exactly the behaviour they had before the feature existed.
        Assert.Empty(CustomDictionary.Parse(CustomDictionary.SeedContents.Split('\n')));
    }

    [Fact]
    public void DeduplicatesIgnoringCase_KeepingTheFirstSpelling()
    {
        IReadOnlyList<string> terms = Words(["Schiphol", "schiphol", "SCHIPHOL"]);
        Assert.Equal("Schiphol", Assert.Single(terms));
    }

    [Fact]
    public void StopsAtTheTermLimit()
    {
        IEnumerable<string> lines = Enumerable.Range(0, CustomDictionary.MaxTerms + 50).Select(i => $"term{i}");
        Assert.Equal(CustomDictionary.MaxTerms, Words(lines).Count);
    }

    [Fact]
    public void SkipsOverlongTerms()
    {
        // Load-bearing rather than cosmetic: DictionaryCorrector stack-allocates its distance
        // matrix from the term length.
        string atLimit = new('a', CustomDictionary.MaxTermLength);
        string overLimit = new('b', CustomDictionary.MaxTermLength + 1);

        IReadOnlyList<string> terms = Words([atLimit, overLimit]);

        Assert.Equal(atLimit, Assert.Single(terms));
    }

    [Fact]
    public void NoLines_ReturnsEmpty()
    {
        Assert.Empty(CustomDictionary.Parse([]));
    }

    [Fact]
    public void ExtractHeader_StopsAtFirstTermLine()
    {
        string header = CustomDictionary.ExtractHeader("# a comment\n\nSchiphol\nGrafana");
        Assert.Equal("# a comment\n\n", header);
    }

    [Fact]
    public void ExtractHeader_EntireSeedFileIsHeader()
    {
        // Every line in the shipped seed is commented out, so a Hub save on a never-touched file
        // must carry the whole thing forward rather than truncating it.
        string header = CustomDictionary.ExtractHeader(CustomDictionary.SeedContents);
        string normalizedSeed = CustomDictionary.SeedContents.Replace("\r\n", "\n").TrimEnd('\n') + "\n";
        Assert.Equal(normalizedSeed, header);
    }

    [Fact]
    public void ExtractHeader_NoLeadingComments_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, CustomDictionary.ExtractHeader("Schiphol\nGrafana"));
    }

    [Fact]
    public void Format_RoundTripsThroughParse()
    {
        string header = CustomDictionary.ExtractHeader(CustomDictionary.SeedContents);
        DictionaryEntry[] entries = [new("Schiphol", []), new("Bluepeak", ["blue peak", "blu peak"])];

        string formatted = CustomDictionary.Format(header, entries);

        Assert.Equal(header, CustomDictionary.ExtractHeader(formatted));
        Assert.Contains("Bluepeak | blue peak, blu peak\n", formatted, StringComparison.Ordinal);
        IReadOnlyList<DictionaryEntry> parsed = CustomDictionary.Parse(formatted.Split('\n'));
        Assert.Equal(["Schiphol", "Bluepeak"], parsed.Select(e => e.Word));
        Assert.Equal(["blue peak", "blu peak"], parsed[1].AlsoHeardAs);
    }

    [Fact]
    public void Format_NoTerms_OmitsTrailingNewlineAfterHeader()
    {
        Assert.Equal("# header\n", CustomDictionary.Format("# header\n", []));
    }

    [Fact]
    public void ReadsAlsoHeardAsAfterTheBar()
    {
        DictionaryEntry entry = Assert.Single(CustomDictionary.Parse(["Bluepeak | blue peak ,  blu peak"]));

        Assert.Equal("Bluepeak", entry.Word);
        Assert.Equal(["blue peak", "blu peak"], entry.AlsoHeardAs);
    }

    [Fact]
    public void AlsoHeardAs_DropsBlanksRepeatsAndTheWordItself()
    {
        IReadOnlyList<string> phrases = CustomDictionary.ParseAlsoHeardAs("Bluepeak", "blue peak, , Blue Peak, bluepeak, blu peak");

        Assert.Equal(["blue peak", "blu peak"], phrases);
    }

    [Fact]
    public void AlsoHeardAs_KeepsAtMostTheLimit()
    {
        string text = string.Join(", ", Enumerable.Range(0, CustomDictionary.MaxAlsoHeardAs + 3).Select(i => $"heard {i}"));

        Assert.Equal(CustomDictionary.MaxAlsoHeardAs, CustomDictionary.ParseAlsoHeardAs("Word", text).Count);
    }

    [Fact]
    public void ALineWithOnlyABar_IsSkipped()
    {
        Assert.Empty(CustomDictionary.Parse([" | blue peak"]));
    }

    private static IReadOnlyList<string> Words(IEnumerable<string> lines) =>
        CustomDictionary.Parse(lines).Select(entry => entry.Word).ToList();
}
