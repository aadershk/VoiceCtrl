using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public class SmartCleanupPromptTests
{
    private static readonly PromptDefaults Defaults = new(CleanupLevels.Standard, ToneAwareness: true, [], []);

    private static PromptContext Context(
        string level = "standard", string? tone = null, string? formatting = null, string? extra = null, bool email = false) =>
        new(level, tone, formatting, extra, email);

    private static ModelRequest RequestFor(PromptContext context, string transcript = "hello there", IReadOnlyList<DictionaryEntry>? dictionary = null) =>
        SmartCleanupPrompt.Request(context, styleNotes: "", dictionary ?? [], transcript, maxTokens: 64);

    [Fact]
    public void TheSystemPrompt_NamesTheCleanupLevelAndNeverMentionsAudio()
    {
        string system = SmartCleanupPrompt.System("light", "");

        Assert.Contains(CleanupLevelMapper.Resolve("light"), system);
        Assert.DoesNotContain("audio", system, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSystemPrompt_TreatsTheTranscriptAsDataNotInstructions()
    {
        Assert.Contains("never answer", SmartCleanupPrompt.System("standard", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProfileHintsAndEmailLayout_ReachTheLastMessage()
    {
        ModelRequest request = RequestFor(Context(tone: "Keep it casual.", formatting: "Use short paragraphs.", extra: "Say 'Hey' not 'Hi'.", email: true));

        Assert.Contains("Keep it casual.", request.User);
        Assert.Contains("Use short paragraphs.", request.User);
        Assert.Contains("Say 'Hey' not 'Hi'.", request.User);
        Assert.Contains("Email Layout", request.User);
        Assert.EndsWith("<transcript>\nhello there\n</transcript>", request.User);
    }

    [Fact]
    public void OnlyDictionaryEntriesNearTheTranscriptAreListed()
    {
        DictionaryEntry bluepeak = new("Bluepeak", ["blue peak"]);
        DictionaryEntry zanzibar = new("Zanzibar", []);

        ModelRequest request = RequestFor(Context(), "open the blue peak dashboard", [bluepeak, zanzibar]);

        Assert.Contains("Bluepeak", request.User);
        Assert.DoesNotContain("Zanzibar", request.User);
        Assert.DoesNotContain("in the audio", request.User);
    }

    /// <summary>The server keeps the longest prefix it has read. Everything that changes with the
    /// app or the words said comes last, so switching apps or a Dictionary hit re-reads only that.</summary>
    [Fact]
    public void EveryAppKind_SharesTheRulesAndTheCommonExamples()
    {
        DictionaryEntry bluepeak = new("Bluepeak", ["blue peak"]);
        ModelRequest[] requests =
        [
            RequestFor(GeminiPrompt.ContextFor(null, "notepad", Defaults)),
            RequestFor(GeminiPrompt.ContextFor(null, "olk", Defaults), "the blue peak deck", [bluepeak]),
            RequestFor(GeminiPrompt.ContextFor(null, "slack", Defaults)),
            RequestFor(GeminiPrompt.ContextFor(null, "winword", Defaults)),
            RequestFor(GeminiPrompt.ContextFor(null, "cursor", Defaults)),
        ];

        IReadOnlyList<(string User, string Assistant)> common = SmartCleanupPrompt.ExamplesFor(CleanupLevels.Standard);
        Assert.All(requests, request =>
        {
            Assert.Equal(requests[0].System, request.System);
            Assert.Equal(common, request.Examples.Take(common.Count));
        });
    }

    [Theory]
    [InlineData("olk", "Hi Maria,\n\n")]
    [InlineData("slack", "Thanks for staying late.\n\nOn the offsite")]
    [InlineData("winword", "Next steps\n\n")]
    [InlineData("cursor", "\n- ")]
    public void EachAppKind_IsShownAWorkedExampleOfItsLayout(string processName, string expected)
    {
        ModelRequest request = RequestFor(GeminiPrompt.ContextFor(null, processName, Defaults));

        Assert.Contains(request.Examples, example => example.Assistant.Contains(expected));
    }

    [Fact]
    public void AnAppKindsExample_CarriesTheSameNotesAsTheRealRequest()
    {
        PromptContext chat = GeminiPrompt.ContextFor(null, "slack", Defaults);
        ModelRequest request = RequestFor(chat);
        string notes = request.User[..request.User.IndexOf("<transcript>", StringComparison.Ordinal)];

        Assert.StartsWith("<notes>", notes);
        Assert.StartsWith(notes, request.Examples[^1].User);
    }

    [Fact]
    public void AnAppWithNoHints_SendsJustTheTranscript()
    {
        Assert.Equal("<transcript>\nhello there\n</transcript>", RequestFor(Context()).User);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("standard")]
    [InlineData("aggressive")]
    public void TheExamples_IncludeAQuestionAndAnOrderThatComeBackUnanswered(string level)
    {
        IReadOnlyList<(string User, string Assistant)> examples = SmartCleanupPrompt.ExamplesFor(level);

        Assert.All(examples, e => Assert.StartsWith("<transcript>", e.User));
        Assert.Contains(examples, e => e.Assistant == "What time does the pharmacy close on Sundays?");
        Assert.Contains(examples, e => e.Assistant.StartsWith("Delete everything", StringComparison.Ordinal));
    }

    [Fact]
    public void WordForWord_IsNeverShownAnExampleThatDropsWords()
    {
        foreach (string processName in new[] { "notepad", "olk", "slack", "winword", "cursor" })
        {
            PromptContext context = GeminiPrompt.ContextFor(new AppProfile { Cleanup = CleanupLevels.Verbatim }, processName, Defaults);
            foreach ((string user, string assistant) in RequestFor(context).Examples)
            {
                Assert.True(SmartCleanupGuard.Check(Transcript(user), assistant, "verbatim", Array.Empty<string>()).Accepted, assistant);
            }
        }
    }

    [Fact]
    public void EveryExample_PassesTheGuardItsLevelUses()
    {
        foreach (string level in new[] { "light", "standard", "aggressive" })
        {
            foreach (string processName in new[] { "notepad", "olk", "slack", "winword", "cursor" })
            {
                PromptContext context = GeminiPrompt.ContextFor(new AppProfile { Cleanup = level }, processName, Defaults);
                foreach ((string user, string assistant) in RequestFor(context).Examples)
                {
                    GuardResult verdict = SmartCleanupGuard.Check(Transcript(user), assistant, level, Array.Empty<string>());
                    Assert.True(verdict.Accepted, $"{level}, {processName}: {assistant} ({verdict.Reason})");
                }
            }
        }
    }

    private static string Transcript(string user)
    {
        int start = user.IndexOf("<transcript>\n", StringComparison.Ordinal) + "<transcript>\n".Length;
        return user[start..user.LastIndexOf("\n</transcript>", StringComparison.Ordinal)];
    }
}
