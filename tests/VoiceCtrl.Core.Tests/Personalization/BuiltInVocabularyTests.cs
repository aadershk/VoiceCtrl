using VoiceCtrl.Core.Personalization;
using Xunit;

namespace VoiceCtrl.Core.Tests.Personalization;

// Same group as the soak test: the 150,000-term speed check allocates a lot, and the soak test
// measures managed memory growth, so the two must not run at the same time.
[Collection("MemorySensitive")]
public class BuiltInVocabularyTests
{
    private static readonly string[] Everyday = ["master", "brook", "by", "the", "we", "land", "at", "called", "is", "on", "a", "in"];

    private static BuiltInVocabulary With(params string[] terms) => new(terms, Everyday);

    [Fact]
    public void Empty_LeavesTextAlone()
    {
        Assert.Equal("we land at Skippel", BuiltInVocabulary.Empty.Apply("we land at Skippel"));
    }

    [Fact]
    public void AnUnknownSpellingThatSoundsLikeAListedTermIsSnappedToIt()
    {
        Assert.Equal("we land at Schiphol", With("Schiphol").Apply("we land at Schipol"));
    }

    [Fact]
    public void ARewrite_IsReportedAsTheWordsHeardAndTheListedSpelling()
    {
        List<(string Heard, string Term)> changes = [];

        With("Schiphol").Apply("we land at Schipol", null, changes);

        Assert.Equal([("Schipol", "Schiphol")], changes);
    }

    [Fact]
    public void NoRewrite_ReportsNothing()
    {
        List<(string Heard, string Term)> changes = [];

        With("Schiphol").Apply("we land at Schiphol", null, changes);

        Assert.Empty(changes);
    }

    [Fact]
    public void AnEverydayWordIsNeverRewritten()
    {
        // "brook" and "Brooke" share a sound, but the speaker said an ordinary word.
        Assert.Equal("by the brook", With("Brooke").Apply("by the brook"));
    }

    [Fact]
    public void AnotherListedTermIsNeverRewrittenIntoItsNeighbour()
    {
        Assert.Equal("Sean called", With("Sean", "Shaun").Apply("Sean called"));
    }

    [Fact]
    public void ACorrectButUnlistedNameIsNotPulledOntoALookalike()
    {
        // Two letters off a listed name is not enough: Grafana is right and merely not on the list.
        Assert.Equal("we use Grafana", With("Gravina").Apply("we use Grafana"));
    }

    [Fact]
    public void APossessiveOfAnEverydayWordIsLeftAlone()
    {
        Assert.Equal("a master's degree", With("Masters").Apply("a master's degree"));
    }

    [Fact]
    public void ALowercasedTermGetsItsCapitals()
    {
        Assert.Equal("we land at Amsterdam", With("Amsterdam").Apply("we land at amsterdam"));
    }

    [Fact]
    public void ACommonWordThatIsAlsoANameKeepsItsCase()
    {
        Assert.Equal("we land at a brook", With("Brook").Apply("we land at a brook"));
    }

    [Fact]
    public void ASplitTermIsJoined()
    {
        Assert.Equal("we run Kubernetes on a", With("Kubernetes").Apply("we run kuber netties on a"));
    }

    [Fact]
    public void WordsThatSpellATermLetterForLetterAreJoinedEvenWhenTheyAreOrdinaryWords()
    {
        Assert.Equal("we tuned LightGBM today", With("LightGBM").Apply("we tuned light GBM today"));
    }

    [Fact]
    public void WordsThatSpellAnOrdinaryWordAreNotJoined()
    {
        Assert.Equal("call any one of them", new BuiltInVocabulary(["Anyone"], [.. Everyday, "anyone", "call", "of", "them"]).Apply("call any one of them"));
    }

    [Fact]
    public void ATermWithInnerCapitalsIsRecasedEvenWhenTheRecogniserCapitalisedIt()
    {
        Assert.Equal("open JupyterLab now", With("JupyterLab").Apply("open Jupyterlab now"));
    }

    [Fact]
    public void ASplitThatSwallowsAnEverydayWordIsLeftAlone()
    {
        Assert.Equal("kuber the netties", With("Kubernetes").Apply("kuber the netties"));
    }

    [Fact]
    public void ATermTooShortToJudgeBySoundIsNotUsed()
    {
        Assert.Equal("we land at Abu", With("Ebo").Apply("we land at Abu"));
    }

    [Fact]
    public void PersonalTermsAreLeftToThePersonalDictionary()
    {
        // The speaker's own spelling is already in the text; the list must not overrule it.
        Assert.Equal("we land at Skipol", With("Schiphol").Apply("we land at Skipol", ["Skipol"]));
    }

    [Fact]
    public void WhenTwoTermsShareASoundTheCloserSpellingWins()
    {
        Assert.Equal("we land at Schiphol", With("Skiphool", "Schiphol").Apply("we land at Schiphoal"));
    }

    [Fact]
    public void SuggestListsTheTermsNearAnUnknownWord()
    {
        DictionaryEntry entry = Assert.Single(With("Schiphol", "Heathrow").Suggest("we land at Skiphol", 5));
        Assert.Equal("Schiphol", entry.Word);
        Assert.Equal(["Skiphol"], entry.AlsoHeardAs);
    }

    [Fact]
    public void SuggestIsCappedAndSkipsAWordTheTextAlreadySpellsRight()
    {
        BuiltInVocabulary vocabulary = With("Schiphol", "Skiphall");
        Assert.Empty(vocabulary.Suggest("we land at Schiphol", 5));
        Assert.Single(vocabulary.Suggest("we land at Skiphol", 1));
    }

    [Fact]
    public void AnObscureTermIsOnlyOfferedToTheModelNeverWrittenByTheRules()
    {
        // 2,000 filler terms ahead of it push "Schiphol" into the unpopular tail.
        string[] terms = [.. Enumerable.Range(0, 2000).Select(n => $"Filler{ToLetters(n)}"), "Schiphol"];
        var vocabulary = new BuiltInVocabulary(terms, Everyday);
        Assert.Equal("we land at Schipol", vocabulary.Apply("we land at Schipol"));
        Assert.Equal("Schiphol", Assert.Single(vocabulary.Suggest("we land at Schipol", 5)).Word);
    }

    [Fact]
    public void ALargeListStaysFast()
    {
        string[] terms = Enumerable.Range(0, 150_000).Select(n => $"Name{ToLetters(n)}").ToArray();
        var vocabulary = new BuiltInVocabulary(terms, Everyday);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int run = 0; run < 100; run++)
        {
            vocabulary.Apply("we land at Skippel and then we call a taxi to the big hotel by the brook near the old canal");
        }

        Assert.True(clock.ElapsedMilliseconds < 1500, $"100 corrections took {clock.ElapsedMilliseconds} ms");
    }

    private static string ToLetters(int n)
    {
        var letters = new System.Text.StringBuilder();
        do
        {
            letters.Insert(0, (char)('a' + n % 26));
            n /= 26;
        }
        while (n > 0);
        return letters.ToString();
    }
}
