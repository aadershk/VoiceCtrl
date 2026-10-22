using VoiceCtrl.Core.Personalization;
using Xunit;

namespace VoiceCtrl.Core.Tests.Personalization;

public class DictionaryCorrectorTests
{
    [Fact]
    public void NoTerms_LeavesTextAlone()
    {
        Assert.Equal("nothing to do here", DictionaryCorrector.Apply("nothing to do here", Array.Empty<string>()));
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
    [InlineData("Grafana", "we use grafting", "we use grafting")]
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

    [Fact]
    public void AlsoHeardAs_BecomesTheWord()
    {
        DictionaryEntry[] dictionary = [new("Bluepeak", ["blue peak"])];

        Assert.Equal("I work at Bluepeak now", DictionaryCorrector.Apply("I work at Blue Peak now", dictionary));
    }

    [Fact]
    public void AlsoHeardAs_MatchesWholeWordsOnly()
    {
        DictionaryEntry[] dictionary = [new("Bluepeak", ["blue peak"])];

        Assert.Equal("true blue peaks", DictionaryCorrector.Apply("true blue peaks", dictionary));
    }

    [Fact]
    public void AlsoHeardAs_LongestPhraseWins()
    {
        DictionaryEntry[] dictionary = [new("Kubernetes", ["cube"]), new("Kubectl", ["cube control"])];

        Assert.Equal("run Kubectl on Kubernetes", DictionaryCorrector.Apply("run cube control on cube", dictionary));
    }

    [Fact]
    public void Entries_StillGetTheNearMissCorrection()
    {
        Assert.Equal("fly from Schiphol", DictionaryCorrector.Apply("fly from schiphol", [new DictionaryEntry("Schiphol", [])]));
    }

    // The recogniser splits and merges names it does not know; the corrector looks one word either
    // side of the term's own length.
    [Theory]
    [InlineData("EconML", "we fit econ ML on it", "we fit EconML on it")]
    [InlineData("XGBoost", "train an XG Boost model", "train an XGBoost model")]
    [InlineData("LightGBM", "then light GBM again", "then LightGBM again")]
    public void RepairsATermTheRecogniserSplit(string term, string text, string expected)
    {
        Assert.Equal(expected, DictionaryCorrector.Apply(text, [term]));
    }

    [Fact]
    public void RepairsATermTheRecogniserRanTogether()
    {
        Assert.Equal("plot it in Scikit Learn", DictionaryCorrector.Apply("plot it in scikitlearn", ["Scikit Learn"]));
    }

    [Fact]
    public void ASplitRepair_NeverSwallowsAnOrdinaryNeighbour()
    {
        Assert.Equal("run Kubectl on Kubernetes", DictionaryCorrector.Apply("run Kubectl on kuber netes", ["Kubernetes", "Kubectl"]));
    }

    [Fact]
    public void SoundAlikeWithADifferentFirstSound_IsLeftAlone()
    {
        Assert.Equal("a pelican", DictionaryCorrector.Apply("a pelican", ["Pelagic"]));
    }
}
