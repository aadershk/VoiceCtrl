using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public class GeminiPromptTests
{
    private static readonly PromptDefaults Defaults = new(CleanupLevels.Standard, ToneAwareness: true, [], []);

    private static string PromptFor(AppProfile? profile, string? processName = "outlook", PromptDefaults? defaults = null) =>
        GeminiPrompt.Build(GeminiPrompt.ContextFor(profile, processName, defaults ?? Defaults), styleNotes: string.Empty, dictionary: []);

    // ---- A list the speaker spelled out survives the app (ticket 41)

    [Theory]
    [InlineData("whatsapp")]
    [InlineData("telegram")]
    [InlineData("notepad")]
    public void TheListRule_AppliesInAnyApp_AndTheChatHintDefersToIt(string processName)
    {
        string prompt = PromptFor(profile: null, processName);

        Assert.Contains("in any application", prompt);
        Assert.Contains("never removes it", prompt);
        if (FormattingHintMapper.IsProse(FormattingHintMapper.Resolve(processName)))
        {
            Assert.Contains("list the speaker spelled out", prompt);
        }
    }

    // ---- Cleanup Level

    [Theory]
    [InlineData(CleanupLevels.Verbatim)]
    [InlineData(CleanupLevels.Light)]
    [InlineData(CleanupLevels.Standard)]
    [InlineData(CleanupLevels.Aggressive)]
    public void AnAppWithNoProfile_UsesTheDefaultCleanup(string level)
    {
        string prompt = PromptFor(profile: null, defaults: Defaults with { DefaultCleanup = level });

        Assert.Contains(CleanupLevelMapper.Resolve(level), prompt);
    }

    [Theory]
    [InlineData(CleanupLevels.Verbatim)]
    [InlineData(CleanupLevels.Light)]
    [InlineData(CleanupLevels.Standard)]
    [InlineData(CleanupLevels.Aggressive)]
    public void AProfilesCleanup_WinsOverTheDefault(string level)
    {
        string other = level == CleanupLevels.Light ? CleanupLevels.Aggressive : CleanupLevels.Light;

        string prompt = PromptFor(new AppProfile { Cleanup = level }, defaults: Defaults with { DefaultCleanup = other });

        Assert.Contains(CleanupLevelMapper.Resolve(level), prompt);
        Assert.DoesNotContain(CleanupLevelMapper.Resolve(other), prompt);
    }

    [Fact]
    public void WordForWord_KeepsFillerWords()
    {
        string prompt = PromptFor(new AppProfile { Cleanup = CleanupLevels.Verbatim });

        Assert.Contains("Transcribe word for word", prompt);
        Assert.Contains("including filler", prompt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    // "Thorough" is the label, not the stored name, so a hand-edited file that says it falls through.
    [InlineData("thorough")]
    public void AProfileWithNoUsableCleanup_FallsThroughToTheDefault(string? cleanup)
    {
        string prompt = PromptFor(new AppProfile { Cleanup = cleanup }, defaults: Defaults with { DefaultCleanup = CleanupLevels.Verbatim });

        Assert.Contains(CleanupLevelMapper.Resolve(CleanupLevels.Verbatim), prompt);
    }

    [Fact]
    public void AHandEditedCleanup_IsReadWhateverItsCase()
    {
        string prompt = PromptFor(new AppProfile { Cleanup = " Verbatim " });

        Assert.Contains(CleanupLevelMapper.Resolve(CleanupLevels.Verbatim), prompt);
    }

    // ---- Email Layout

    [Fact]
    public void EmailLayout_LaysTheTextOutAsAnEmail()
    {
        string prompt = PromptFor(new AppProfile { EmailLayout = true });

        Assert.Contains("Email Layout", prompt);
        Assert.Contains("greeting", prompt);
        Assert.Contains("sign-off", prompt);
    }

    [Fact]
    public void EmailLayout_IsStructuralOnly_AndUsesANameOnlyIfOneWasSpoken()
    {
        string prompt = PromptFor(new AppProfile { EmailLayout = true });

        Assert.Contains("never add a word the speaker did not say", prompt);
        Assert.Contains("a name only if the speaker said it", prompt);
    }

    [Fact]
    public void EmailLayoutOff_AddsNothing()
    {
        string prompt = PromptFor(new AppProfile { EmailLayout = false });

        Assert.DoesNotContain("Email Layout", prompt);
    }

    [Fact]
    public void AnEmailAppWithNoProfile_GetsEmailLayout()
    {
        // Ticket 55: email apps are laid out as emails by default.
        Assert.Contains("Email Layout", PromptFor(profile: null));
        Assert.Contains("Email Layout", PromptFor(new AppProfile { EmailLayout = null }));
    }

    [Fact]
    public void AnotherAppWithNoProfile_GetsNoEmailLayout()
    {
        Assert.DoesNotContain("Email Layout", PromptFor(profile: null, processName: "notepad"));
    }

    [Fact]
    public void EmailLayout_KeepsTheProfilesOtherFields()
    {
        var profile = new AppProfile { Tone = "formal", Formatting = AppProfile.Prose, Cleanup = CleanupLevels.Light, EmailLayout = true };

        PromptContext context = GeminiPrompt.ContextFor(profile, "outlook", Defaults);

        Assert.True(context.EmailLayout);
        Assert.Equal(CleanupLevels.Light, context.CleanupLevel);
        Assert.Equal("formal", context.ToneHint);
        Assert.NotNull(context.FormattingHint);
    }

    // ---- Press Enter

    [Fact]
    public void PressEnterOn_AsksForAFinalPressEnterToBeKeptAsWords()
    {
        string prompt = PromptFor(profile: null, defaults: Defaults with { PressEnter = true });

        Assert.Contains("Press Enter", prompt);
        Assert.Contains("\"press enter\"", prompt);
    }

    [Fact]
    public void PressEnterOff_AddsNothing()
    {
        Assert.DoesNotContain("Press Enter", PromptFor(profile: null));
    }

    [Fact]
    public void PressEnter_AppliesWhateverTheProfile()
    {
        PromptContext context = GeminiPrompt.ContextFor(new AppProfile { EmailLayout = true }, "outlook", Defaults with { PressEnter = true });

        Assert.True(context.PressEnter);
    }

    // ---- Tone and Formatting (unchanged by this slice, pinned so the move out of the client is safe)

    [Fact]
    public void ToneNone_SuppressesTheBuiltInTone()
    {
        Assert.Null(GeminiPrompt.ContextFor(new AppProfile { Tone = "none" }, "slack", Defaults).ToneHint);
        Assert.NotNull(GeminiPrompt.ContextFor(null, "slack", Defaults).ToneHint);
    }

    [Fact]
    public void AnUnknownFormatting_FallsBackToTheBuiltInOne()
    {
        PromptContext context = GeminiPrompt.ContextFor(new AppProfile { Formatting = "bulleted" }, "slack", Defaults);

        Assert.Equal(FormattingHintMapper.ResolveExplicit(AppProfile.Prose, AppKind.WorkChat), context.FormattingHint);
    }
}
