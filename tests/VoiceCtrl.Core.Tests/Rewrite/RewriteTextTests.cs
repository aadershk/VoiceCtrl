using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Rewrite;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Rewrite;

public class RewriteTextTests
{
    [Fact]
    public void Clean_PlainReply_IsKept()
    {
        Assert.Equal("Could you check the file?", RewriteText.Clean("Could you check the file?", "Please check the file."));
    }

    [Theory]
    [InlineData("<think>\n\n</think>\n\nShort one.", "Short one.")]
    [InlineData("<text>\nShort one.\n</text>", "Short one.")]
    [InlineData("\"Short one.\"", "Short one.")]
    [InlineData("Sure! Here is the shorter text:\nShort one.", "Short one.")]
    [InlineData("  Short one.  \n", "Short one.")]
    public void Clean_TakesOffWhatModelsWrapAnAnswerIn(string reply, string expected)
    {
        Assert.Equal(expected, RewriteText.Clean(reply, "A much longer original text here."));
    }

    [Fact]
    public void Clean_KeepsQuotesWhenTheSourceHadThem()
    {
        Assert.Equal("\"Hi,\" she said.", RewriteText.Clean("\"Hi,\" she said.", "She said \"hi\"."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("<think>only thoughts</think>")]
    public void Clean_NothingUsable_IsNull(string? reply)
    {
        Assert.Null(RewriteText.Clean(reply, "Some text."));
    }

    [Fact]
    public void Clean_AModelRunningOn_IsNull()
    {
        Assert.Null(RewriteText.Clean(new string('x', 5000), "Short."));
    }

    [Fact]
    public void System_ListsTheDictionaryWordsOnlyWhenThereAreSome()
    {
        Assert.DoesNotContain("Spell these", RewriteText.System([]));
        Assert.Contains("Bluepeak", RewriteText.System([new DictionaryEntry("Bluepeak", [])]));
    }

    [Fact]
    public void System_TellsBothEnginesToNestAListUnderItsGroups()
    {
        // Ticket 58: "make it a checklist" on plans by day gives each day once, its plans under it.
        Assert.Contains("indent its items under it", RewriteText.System([]));
        Assert.Contains("indent its items under it", RewriteText.GeminiPrompt([], "Hello there."));
    }

    [Fact]
    public void ExamplePairs_ShowAGroupedListNestedAndAListWithoutGroupsFlat()
    {
        string[] lists = RewriteText.ExamplePairs.Select(pair => pair.Assistant).Where(answer => answer.StartsWith("- ", StringComparison.Ordinal)).ToArray();

        Assert.Contains(lists, answer => answer.Contains(":\n  - ", StringComparison.Ordinal));
        Assert.Contains(lists, answer => !answer.Contains("\n  ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("On Monday I have to renew my passport, return the library books and pick up the dry cleaning.",
        "- Renew my passport\n- Return the library books\n- Pick up the dry cleaning",
        "- On Monday:\n  - Renew my passport\n  - Return the library books\n  - Pick up the dry cleaning")]
    [InlineData("Tomorrow morning I need to call the landlord, pay the gas bill and book a haircut.",
        "- Call the landlord\n- Pay the gas bill\n- Book a haircut",
        "- Tomorrow morning:\n  - Call the landlord\n  - Pay the gas bill\n  - Book a haircut")]
    [InlineData("For the kitchen, we need a new kettle, two chairs and a bigger bin.",
        "- A new kettle\n- Two chairs\n- A bigger bin",
        "- For the kitchen:\n  - A new kettle\n  - Two chairs\n  - A bigger bin")]
    [InlineData("Before Friday's demo I want to fix the crash on startup, record the video and update the slides.",
        "1. Fix the crash on startup\n2. Record the video\n3. Update the slides",
        "- Before Friday's demo:\n  1. Fix the crash on startup\n  2. Record the video\n  3. Update the slides")]
    public void Clean_PutsBackTheOneGroupAListDropped(string source, string reply, string expected)
    {
        // Ticket 58: the local model often drops the day or place the speaker opened with.
        Assert.Equal(expected, RewriteText.Clean(reply, source));
    }

    [Theory]
    [InlineData("On Monday I have to renew my passport and return the books.", "- Renew my passport on Monday\n- Return the books")]
    [InlineData("On Monday I have to renew my passport and return the books.", "- On Monday:\n  - Renew my passport\n  - Return the books")]
    [InlineData("On Monday I have to renew my passport and return the books.", "Renew my passport and return the books.")]
    [InlineData("On Monday I have to renew my passport and return the books.", "- Renovar mi pasaporte\n- Devolver los libros")]
    [InlineData("I need to buy milk, eggs and bread.", "- Buy milk\n- Buy eggs\n- Buy bread")]
    public void Clean_LeavesAListThatKeptItsGroupOrHadNone(string source, string reply)
    {
        Assert.Equal(reply, RewriteText.Clean(reply, source));
    }

    [Fact]
    public void User_CarriesTheInstructionAndFencesTheText()
    {
        string user = RewriteText.User("  make this shorter ", "Hello there.");

        Assert.Equal("<text>\nHello there.\n</text>\n\nInstruction: make this shorter", user);
    }

    [Fact]
    public void GeminiPrompt_TellsItToListenAndHandlesSilence()
    {
        string prompt = RewriteText.GeminiPrompt([], "Hello there.");

        Assert.Contains("[NO_SPEECH_DETECTED]", prompt);
        Assert.Contains("<text>\nHello there.\n</text>", prompt);
    }
}

public class RewriteTimeoutTests
{
    [Theory]
    [InlineData(SmartCleanupTier.Standard)]
    [InlineData(SmartCleanupTier.Better)]
    public void IsLongEnoughForAShortAnswerFromAOneLineText(SmartCleanupTier tier)
    {
        // "turn this into an email" from 13 words writes about 70: the bench's first timeouts hit that.
        Assert.True(SmartCleanupService.RewriteTimeoutFor(tier, 13) >= TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void GrowsWithTheText_AndIsCapped()
    {
        Assert.True(SmartCleanupService.RewriteTimeoutFor(SmartCleanupTier.Better, 400) > SmartCleanupService.RewriteTimeoutFor(SmartCleanupTier.Better, 40));
        Assert.Equal(TimeSpan.FromSeconds(60), SmartCleanupService.RewriteTimeoutFor(SmartCleanupTier.Better, 100_000));
    }

    [Fact]
    public void BetterWaitsLongerThanStandard_BecauseItDecodesSlower()
    {
        Assert.True(SmartCleanupService.RewriteTimeoutFor(SmartCleanupTier.Better, 100) > SmartCleanupService.RewriteTimeoutFor(SmartCleanupTier.Standard, 100));
    }
}
