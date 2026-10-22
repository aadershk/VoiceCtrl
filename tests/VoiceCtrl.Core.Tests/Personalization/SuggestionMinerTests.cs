using VoiceCtrl.Core.Personalization;

namespace VoiceCtrl.Core.Tests.Personalization;

public sealed class SuggestionMinerTests
{
    private static IReadOnlyList<SuggestionCandidate> Find(params string[] dictations) =>
        SuggestionMiner.Find(dictations, exclude: []);

    private static SuggestionCandidate? Named(IReadOnlyList<SuggestionCandidate> found, string word) =>
        found.SingleOrDefault(c => c.Word == word);

    [Fact]
    public void ANameSaidMidSentence_IsACandidate_CountedOncePerDictation()
    {
        IReadOnlyList<SuggestionCandidate> found = Find(
            "Can you send this to Maya Ortiz today.",
            "I spoke with Maya Ortiz and Maya Ortiz agreed.",
            "Ask Maya Ortiz about the slides.");

        Assert.Equal(3, Named(found, "Maya Ortiz")?.Dictations);
    }

    [Fact]
    public void AWordCapitalisedOnlyBecauseItStartsASentence_IsNotACandidate()
    {
        IReadOnlyList<SuggestionCandidate> found = Find(
            "Thanks for the notes. Thanks again.",
            "Thanks, that works.",
            "Sure. Thanks!");

        Assert.Null(Named(found, "Thanks"));
        Assert.Null(Named(found, "Sure"));
    }

    [Fact]
    public void ANameStartingASentence_CountsWhenTheSameNameAlsoAppearsMidSentence()
    {
        IReadOnlyList<SuggestionCandidate> found = Find(
            "Maya Ortiz said yes.",
            "Please thank Maya Ortiz.");

        Assert.Equal(2, Named(found, "Maya Ortiz")?.Dictations);
    }

    [Fact]
    public void AWordAlsoUsedInLowercase_IsAnOrdinaryWord()
    {
        IReadOnlyList<SuggestionCandidate> found = Find(
            "We call the motion Glide in the spec.",
            "It should glide into place.",
            "Keep Glide as the name.");

        Assert.Null(Named(found, "Glide"));
    }

    [Theory]
    [InlineData("AGIFORS")]
    [InlineData("VoiceCtrl")]
    [InlineData("A320")]
    [InlineData("econML")]
    [InlineData("iPhone")]
    public void AcronymsMixedCaseAndDigitWords_AreCandidatesAnywhere(string word)
    {
        IReadOnlyList<SuggestionCandidate> found = Find($"{word} is on the list.", $"We use {word} daily.");

        Assert.Equal(2, Named(found, word)?.Dictations);
    }

    [Fact]
    public void DaysMonthsTheWordIAndCommonAcronyms_AreNeverCandidates()
    {
        IReadOnlyList<SuggestionCandidate> found = Find(
            "See you on Monday in March, I think.",
            "Is Friday OK for the PDF, or do I send the URL in April?",
            "Let's meet Monday. I'm free.");

        Assert.Empty(found);
    }

    [Fact]
    public void WordsAlreadyInTheDictionaryOrIgnored_AreLeftOut_IgnoringCase()
    {
        IReadOnlyList<SuggestionCandidate> found = SuggestionMiner.Find(
            ["Send it to Maya Ortiz and Lisbon Ops, cc AGIFORS and Bluepeak."],
            exclude: ["maya ortiz", "AGIFORS", "Lisbon ops"]);

        Assert.Equal(["Bluepeak"], found.Select(c => c.Word));
    }

    [Fact]
    public void APartOfAWordAlreadyInTheDictionary_IsLeftOut()
    {
        IReadOnlyList<SuggestionCandidate> found = SuggestionMiner.Find(
            ["Ortiz said yes. Then ask Ortiz again."],
            exclude: ["Maya Ortiz"]);

        Assert.Empty(found);
    }

    [Fact]
    public void ThePossessive_IsDropped()
    {
        IReadOnlyList<SuggestionCandidate> found = Find("That was Bluepeak's call.", "Ask Bluepeak.");

        Assert.Equal(2, Named(found, "Bluepeak")?.Dictations);
    }

    [Fact]
    public void TheSpellingShown_IsTheOneUsedMost()
    {
        IReadOnlyList<SuggestionCandidate> found = Find("Ask about VoiceCtrl now.", "Ask about VoiceCTRL now.", "Ask about VoiceCtrl again.");

        Assert.Equal(3, Named(found, "VoiceCtrl")?.Dictations);
        Assert.Null(Named(found, "VoiceCTRL"));
    }

    [Fact]
    public void PunctuationBreaksARun()
    {
        IReadOnlyList<SuggestionCandidate> found = Find("We flew to Lisbon, Porto and Faro.");

        Assert.Equal(["Faro", "Lisbon", "Porto"], found.Select(c => c.Word).Order());
    }

    [Fact]
    public void ARunIsAtMostThreeWords()
    {
        IReadOnlyList<SuggestionCandidate> found = Find("We met the Amsterdam Schiphol Airport Operations Team there.");

        Assert.All(found, c => Assert.True(c.Word.Split(' ').Length <= 3));
    }

    [Fact]
    public void TheExample_IsTheSentenceItWasSaidIn()
    {
        IReadOnlyList<SuggestionCandidate> found = Find("First part. Then we asked Bluepeak for the numbers. Last part.");

        Assert.Equal("Then we asked Bluepeak for the numbers.", Named(found, "Bluepeak")?.Example);
    }

    [Fact]
    public void ALongExample_IsCutToAWindowAroundTheWord()
    {
        string filler = string.Join(" ", Enumerable.Repeat("words", 80));
        IReadOnlyList<SuggestionCandidate> found = Find($"{filler} Bluepeak {filler}.");

        string example = Named(found, "Bluepeak")!.Example;
        Assert.Contains("Bluepeak", example);
        Assert.True(example.Length <= SuggestionMiner.MaxExampleLength);
    }

    [Fact]
    public void MostRecurringFirst()
    {
        IReadOnlyList<SuggestionCandidate> found = Find(
            "Ask Bluepeak.", "Ask Bluepeak and Lisbon Ops.", "Ask Bluepeak.");

        Assert.Equal(["Bluepeak", "Lisbon Ops"], found.Select(c => c.Word));
    }
}
