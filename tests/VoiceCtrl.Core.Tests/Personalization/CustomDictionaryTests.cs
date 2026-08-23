using VoiceCtrl.Core.Personalization;
using Xunit;

namespace VoiceCtrl.Core.Tests.Personalization;

public class CustomDictionaryTests
{
    [Fact]
    public void ReadsOneTermPerLine()
    {
        IReadOnlyList<string> terms = CustomDictionary.Parse(["Schiphol", "Grafana"]);
        Assert.Equal(new[] { "Schiphol", "Grafana" }, terms.ToArray());
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        IReadOnlyList<string> terms = CustomDictionary.Parse(["  Schiphol\t"]);
        Assert.Equal("Schiphol", Assert.Single(terms));
    }

    [Fact]
    public void SkipsBlankAndCommentLines()
    {
        IReadOnlyList<string> terms = CustomDictionary.Parse(["# a comment", "", "   ", "Schiphol"]);
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
        IReadOnlyList<string> terms = CustomDictionary.Parse(["Schiphol", "schiphol", "SCHIPHOL"]);
        Assert.Equal("Schiphol", Assert.Single(terms));
    }

    [Fact]
    public void StopsAtTheTermLimit()
    {
        IEnumerable<string> lines = Enumerable.Range(0, CustomDictionary.MaxTerms + 50).Select(i => $"term{i}");
        Assert.Equal(CustomDictionary.MaxTerms, CustomDictionary.Parse(lines).Count);
    }

    [Fact]
    public void SkipsOverlongTerms()
    {
        // Load-bearing rather than cosmetic: DictionaryCorrector stack-allocates its distance
        // matrix from the term length.
        string atLimit = new('a', CustomDictionary.MaxTermLength);
        string overLimit = new('b', CustomDictionary.MaxTermLength + 1);

        IReadOnlyList<string> terms = CustomDictionary.Parse([atLimit, overLimit]);

        Assert.Equal(atLimit, Assert.Single(terms));
    }

    [Fact]
    public void NoLines_ReturnsEmpty()
    {
        Assert.Empty(CustomDictionary.Parse([]));
    }
}
