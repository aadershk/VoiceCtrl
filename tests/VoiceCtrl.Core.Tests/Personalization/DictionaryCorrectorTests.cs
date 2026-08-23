using VoiceCtrl.Core.Personalization;
using Xunit;

namespace VoiceCtrl.Core.Tests.Personalization;

public class DictionaryCorrectorTests
{
    [Fact]
    public void NoTerms_LeavesTextAlone()
    {
        Assert.Equal("nothing to do here", DictionaryCorrector.Apply("nothing to do here", []));
    }

    [Fact]
    public void EmptyText_IsHandled()
    {
        Assert.Equal(string.Empty, DictionaryCorrector.Apply(string.Empty, ["Schiphol"]));
    }

    [Fact]
    public void FixesCasingOnAnExactMatch()
    {
        Assert.Equal("the iOS build", DictionaryCorrector.Apply("the ios build", ["iOS"]));
    }

    [Fact]
    public void CasingFix_AppliesEvenToTermsTooShortForFuzzyMatching()
    {
        // A four-letter term gets no edit budget at all, but rewriting the same letters into the
        // spelling the user asked for carries no risk, so it bypasses every guard.
        Assert.Equal("running on ARM today", DictionaryCorrector.Apply("running on arm today", ["ARM"]));
    }

    [Fact]
    public void AlreadyCorrect_IsNotRewritten()
    {
        Assert.Equal("deploy to Schiphol", DictionaryCorrector.Apply("deploy to Schiphol", ["Schiphol"]));
    }

    [Fact]
    public void CorrectsANearMissWithinTheEditBudget()
    {
        Assert.Equal("we fly into Schiphol", DictionaryCorrector.Apply("we fly into Schipol", ["Schiphol"]));
    }

    [Fact]
    public void CorrectsAcrossAdjacentPunctuation()
    {
        Assert.Equal("we use Grafana, daily", DictionaryCorrector.Apply("we use Grafna, daily", ["Grafana"]));
    }

    [Fact]
    public void CorrectsEveryOccurrenceInOnePass()
    {
        Assert.Equal(
            "deploy Grafana and Datadog",
            DictionaryCorrector.Apply("deploy Grafna and Datadg", ["Grafana", "Datadog"]));
    }

    [Fact]
    public void ShortTerms_GetNoEditBudget()
    {
        // "for" is one edit from "Ford". At four characters there is no distance threshold that
        // catches genuine mishearings without also rewriting ordinary English, so short terms only
        // ever match exactly.
        Assert.Equal("we drove for a while", DictionaryCorrector.Apply("we drove for a while", ["Ford"]));
    }

    [Fact]
    public void CommonWords_AreNotRewrittenEvenWithinBudget()
    {
        // "thought" is one edit from "Thoughts" and well inside the budget for an eight-letter
        // term. The blocklist is the only thing stopping this.
        Assert.Equal("i had a thought", DictionaryCorrector.Apply("i had a thought", ["Thoughts"]));
    }

    [Fact]
    public void DifferenceLargerThanTheBudget_IsLeftAlone()
    {
        Assert.Equal("we saw a pelican", DictionaryCorrector.Apply("we saw a pelican", ["Postgres"]));
    }

    // The budget scales with the term's length, so the same two-character miss is a correction on a
    // long name and a refusal on a short one.
    [Theory]
    [InlineData("Grafana", "we use grafna", "we use Grafana")]
    [InlineData("Grafana", "we use grfna", "we use grfna")]
    [InlineData("Kubernetes", "run it on kubrnets", "run it on Kubernetes")]
    public void EditBudgetScalesWithTermLength(string term, string text, string expected)
    {
        Assert.Equal(expected, DictionaryCorrector.Apply(text, [term]));
    }

    [Fact]
    public void MultiWordTermClaimsItsSpanBeforeASingleWordTermCanTakePartOfIt()
    {
        string result = DictionaryCorrector.Apply(
            "flying from amsterdam schipol tomorrow", ["Schiphol", "Amsterdam Schiphol"]);

        Assert.Equal("flying from Amsterdam Schiphol tomorrow", result);
    }

    [Fact]
    public void OrderOfTermsInTheFileDoesNotChangeTheOutcome()
    {
        string oneOrder = DictionaryCorrector.Apply(
            "at amsterdam schipol", ["Amsterdam Schiphol", "Schiphol"]);
        string otherOrder = DictionaryCorrector.Apply(
            "at amsterdam schipol", ["Schiphol", "Amsterdam Schiphol"]);

        Assert.Equal(oneOrder, otherOrder);
    }

    [Fact]
    public void AnOverlongTermIsIgnoredRatherThanSizingABuffer()
    {
        // Apply is public, so it has to survive a list that never went through CustomDictionary.
        string term = new('a', CustomDictionary.MaxTermLength + 1);
        string text = new('a', CustomDictionary.MaxTermLength);

        Assert.Equal(text, DictionaryCorrector.Apply(text, [term]));
    }

    [Fact]
    public void TextWithoutWords_IsReturnedUnchanged()
    {
        Assert.Equal("... !!", DictionaryCorrector.Apply("... !!", ["Schiphol"]));
    }
}
