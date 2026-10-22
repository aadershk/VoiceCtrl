using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

/// <summary>Ticket 56: the tone the model is told follows the app's Style; AI prompts and code keep
/// their words and casing.</summary>
public class ToneHintMapperTests
{
    private static readonly PromptDefaults Defaults = new(CleanupLevels.Standard, ToneAwareness: true, [], []);

    private static string? HintFor(string processName, StyleSettings? styles = null) =>
        GeminiPrompt.ContextFor(null, processName, Defaults with { Styles = styles }).ToneHint;

    [Fact]
    public void AChatApp_GetsTheCasualHint()
    {
        Assert.Contains("casual", HintFor("slack"));
    }

    [Fact]
    public void AnEmailApp_GetsTheProfessionalHint()
    {
        Assert.Contains("professional", HintFor("outlook"));
    }

    [Fact]
    public void ACodeEditor_GetsTheCodeHint()
    {
        Assert.Contains("code comment", HintFor("devenv"));
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        Assert.Equal(HintFor("slack"), HintFor("SLACK"));
    }

    [Fact]
    public void AnUnknownApp_GetsTheOtherAppsStyle()
    {
        // Formal adds no note of its own: told to be formal, the model rephrases (ticket 56).
        Assert.Null(HintFor("some_random_app"));
        Assert.Contains("very casual", HintFor("some_random_app", new StyleSettings { Other = Style.VeryCasual }));
    }

    [Fact]
    public void AStyleChangedOnTheStyleScreen_ChangesTheHint()
    {
        Assert.Contains("upbeat", HintFor("slack", new StyleSettings { WorkChat = Style.Excited }));
        Assert.Contains("casual", HintFor("whatsapp", new StyleSettings { WorkChat = Style.Excited }));
    }

    [Fact]
    public void WithToneAwarenessOff_NoHintIsGuessed()
    {
        Assert.Null(GeminiPrompt.ContextFor(null, "slack", Defaults with { ToneAwareness = false }).ToneHint);
    }
}
