using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public class SmartCleanupMenderTests
{
    private static readonly BuiltInVocabulary Vocabulary = new(
        ["Cologne", "Schiphol", "Eindhoven", "Parquet", "Datadog"],
        [
            "we", "flew", "to", "and", "then", "took", "the", "train", "for", "a", "weekend", "met", "staff", "near", "files", "read",
            "table", "six", "sex", "whole", "hole", "lead", "led", "model", "mode", "there", "their", "team", "time", "was", "long",
            "data", "agreement", "apple", "idea", "hour", "university", "one", "i", "booked", "tonight", "asked", "about", "today",
            "use", "plots", "every", "day", "fits", "scales", "well", "were", "fine", "of", "mode", "sign", "an", "soon", "need",
        ]);

    private static string Mend(string text, string reply, params string[] dictionary)
    {
        IReadOnlyList<string> unknown = SmartCleanupMender.UnknownWords(text, Vocabulary, dictionary);
        return SmartCleanupMender.Apply(text, SmartCleanupMender.Parse(reply), unknown, Vocabulary, dictionary);
    }

    [Fact]
    public void Apply_ReportsThePairsItAppliedAndNotTheOnesItRefused()
    {
        const string text = "The hole team met the led today.";
        IReadOnlyList<string> unknown = SmartCleanupMender.UnknownWords(text, Vocabulary, []);
        List<WordMend> applied = [];

        SmartCleanupMender.Apply(text, SmartCleanupMender.Parse("hole -> whole\nled -> lead"), unknown, Vocabulary, [], applied);

        Assert.Equal([new WordMend("hole", "whole")], applied);
    }

    [Fact]
    public void Parse_ReadsPairsAndIgnoresEverythingElse()
    {
        IReadOnlyList<WordMend> mends = SmartCleanupMender.Parse("Sure! Here you go:\n- hole -> whole\n2. \"their\" → \"there\".\nnone\nno change needed");

        Assert.Equal([new WordMend("hole", "whole"), new WordMend("their", "there")], mends);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    public void Parse_AnEmptyOrNoneReply_HasNoPairs(string? reply) =>
        Assert.Empty(SmartCleanupMender.Parse(reply));

    [Fact]
    public void UnknownWords_AreTheOnesTheListNeverSaw_NotNamesItKnows()
    {
        IReadOnlyList<string> unknown = SmartCleanupMender.UnknownWords(
            "We flew to Colonbon and met Schiphol staff near the parricate files.", Vocabulary, []);

        Assert.Equal(["colonbon", "parricate"], unknown);
    }

    [Fact]
    public void UnknownWords_LeaveOutTheUsersDictionary() =>
        Assert.Empty(SmartCleanupMender.UnknownWords("We met the Vedarth team today.", Vocabulary, ["Vedarth"]));

    [Fact]
    public void ANameSlip_BecomesTheListedSpelling_InTheOldWordsPlace() =>
        Assert.Equal("We flew to Eindhoven and then took the train.", Mend("We flew to Eindovind and then took the train.", "eindovind -> eindhoven"));

    [Fact]
    public void ALowerCaseSlip_BecomesAListedTerm() =>
        Assert.Equal("We read the Parquet files today.", Mend("We read the parricate files today.", "parricate -> parquet"));

    [Fact]
    public void ASoundAlikeInOrdinaryEnglish_IsApplied() =>
        Assert.Equal("I booked a table for six tonight.", Mend("I booked a table for sex tonight.", "sex -> six"));

    [Fact]
    public void OnlyOneOrdinaryWord_IsMendedPerText() =>
        Assert.Equal("The whole team met the led today.", Mend("The hole team met the led today.", "hole -> whole\nled -> lead"));

    [Fact]
    public void ACapitalisedOrdinaryWordInsideASentence_IsAName_AndStays() =>
        Assert.Equal("I asked Hole about the model today.", Mend("I asked Hole about the model today.", "hole -> whole"));

    [Fact]
    public void ADictionaryWord_IsNeverMended() =>
        Assert.Equal("I use mode for the plots every day.", Mend("I use mode for the plots every day.", "mode -> model", "Mode"));

    [Fact]
    public void ANewWordTheListDoesNotKnow_IsRefused() =>
        Assert.Equal("We flew to Colonbon and then took the train.", Mend("We flew to Colonbon and then took the train.", "colonbon -> kolonbonn"));

    [Fact]
    public void ANewWordThatSoundsNothingLikeTheOld_IsRefused() =>
        Assert.Equal("We flew to Colonbon and then took the train.", Mend("We flew to Colonbon and then took the train.", "colonbon -> agreement"));

    [Fact]
    public void AnOrdinaryWordMayNotBecomeAName() =>
        Assert.Equal("We read the mode files today.", Mend("We read the mode files today.", "mode -> datadog"));

    [Fact]
    public void MoreCorrectionsThanTheTextCanHold_MeansTheModelIsRewriting_AndNoneApply()
    {
        const string text = "We flew to Colonbon and met the Eindovind staff for a weekend near Parricate.";

        Assert.Equal(text, Mend(text, "colonbon -> cologne\neindovind -> eindhoven\nparricate -> parquet\nthe -> a\nweekend -> week\nmet -> meet"));
    }

    [Fact]
    public void AMendThatWouldRepeatTheNeighbouringWord_IsSkipped() =>
        Assert.Equal("We flew to Eindovind Eindhoven today.", Mend("We flew to Eindovind Eindhoven today.", "eindovind -> eindhoven"));

    [Fact]
    public void EveryOccurrenceOfAMendedWord_IsReplaced() =>
        Assert.Equal("The Datadog fits and the Datadog scales well.", Mend("The detadog fits and the detadog scales well.", "detadog -> datadog"));

    [Theory]
    [InlineData("We need to sign an data agreement soon.", "We need to sign a data agreement soon.")]
    [InlineData("She had a idea about the table.", "She had an idea about the table.")]
    [InlineData("An table was free.", "A table was free.")]
    [InlineData("It took a hour and one apple.", "It took a hour and one apple.")]
    [InlineData("She studies at a university and an hour is enough.", "She studies at a university and an hour is enough.")]
    [InlineData("He works at an MBA school.", "He works at an MBA school.")]
    [InlineData("It was a one off.", "It was a one off.")]
    public void ArticleAgreement_FixesOnlyWhatEnglishSpellsAsItSounds(string text, string expected) =>
        Assert.Equal(expected, ArticleAgreement.Apply(text, Vocabulary.IsOrdinaryWord));
}
