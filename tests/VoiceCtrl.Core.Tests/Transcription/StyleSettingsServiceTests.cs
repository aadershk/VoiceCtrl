using System.IO;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

/// <summary>Ticket 56: the Style screen's groups, examples, name and moved apps.</summary>
public class StyleSettingsServiceTests
{
    private static readonly HashSet<string> Ordinary = new(StringComparer.Ordinal) { "yeah", "can", "we", "hi", "best", "let", "don't", "just" };

    private static StyleSettingsService Service(out SettingsStore store)
    {
        store = SettingsStore.InMemory(new UserSettings());
        return new StyleSettingsService(store, Ordinary.Contains);
    }

    private static StyleGroup Group(StyleView view, AppKind kind) => view.Groups.Single(g => g.Kind == kind);

    [Fact]
    public void AFreshInstall_ShowsTheOwnersDefaults()
    {
        StyleView view = Service(out _).View();

        Assert.Equal([AppKind.PersonalChat, AppKind.WorkChat, AppKind.Email, AppKind.Document, AppKind.Other, AppKind.AiPrompt], view.Groups.Select(g => g.Kind));
        Assert.Equal(Style.Casual, Group(view, AppKind.PersonalChat).Style);
        Assert.Equal(Style.Casual, Group(view, AppKind.WorkChat).Style);
        Assert.Equal(Style.Formal, Group(view, AppKind.Email).Style);
        Assert.Equal(Style.Formal, Group(view, AppKind.Document).Style);
        Assert.Equal(Style.Formal, Group(view, AppKind.Other).Style);
        Assert.Null(Group(view, AppKind.AiPrompt).Style);
        Assert.Equal(string.Empty, view.YourName);
    }

    [Fact]
    public void TheOwnersAppLists_AreOnTheirCards()
    {
        StyleView view = Service(out _).View();

        Assert.Equal(["Slack", "Microsoft Teams", "Google Chat", "Element"], Group(view, AppKind.WorkChat).Apps.Select(a => a.Name));
        Assert.Subset(Group(view, AppKind.PersonalChat).Apps.Select(a => a.Name).ToHashSet(),
            new HashSet<string> { "WhatsApp", "Telegram", "Signal", "Messenger", "Discord" });
        Assert.Contains("Gmail", Group(view, AppKind.Email).Apps.Select(a => a.Name));
    }

    [Fact]
    public void EachCard_ShowsItsExampleInEveryStyle_AsTheEngineWritesIt()
    {
        StyleGroup chat = Group(Service(out _).View(), AppKind.PersonalChat);

        Assert.Equal(
            ["Yes, sounds good. I will be there in five minutes.", "Yeah, sounds good. I'll be there in five minutes",
             "yeah, sounds good. I'll be there in five minutes", "Yeah, sounds good! I'll be there in five minutes!"],
            chat.Examples.Select(e => e.Text));
        Assert.Equal([Style.Formal, Style.Casual, Style.VeryCasual, Style.Excited], chat.Examples.Select(e => e.Style!.Value));

        // Ticket 59: a work chat's short message is one paragraph too (the UI mock copies these).
        Assert.Equal(
            ["I have pushed the fix. Can you take a look when you are free?", "I've pushed the fix. Can you take a look when you're free?",
             "I've pushed the fix. can you take a look when you're free?", "I've pushed the fix! Can you take a look when you're free?"],
            Group(Service(out _).View(), AppKind.WorkChat).Examples.Select(e => e.Text));
    }

    [Fact]
    public void TheEmailExample_IsLaidOutAsAnEmail()
    {
        StyleExample formal = Group(Service(out _).View(), AppKind.Email).Examples[0];

        Assert.Equal("Hi Sam,\n\nThanks for sending this over. I will read it tonight and get back to you tomorrow.\n\nBest,\nAlex", formal.Text);
    }

    [Fact]
    public void AiPrompts_HaveOneExample_KeptAsSaid()
    {
        StyleGroup prompts = Group(Service(out _).View(), AppKind.AiPrompt);

        Assert.Equal("Don't rename the API client. Just fix the retry logic.", Assert.Single(prompts.Examples).Text);
    }

    [Fact]
    public void ChangingAStyle_IsSaved()
    {
        StyleSettingsService service = Service(out SettingsStore store);

        StyleView view = service.SetStyle(AppKind.WorkChat, Style.Formal);

        Assert.Equal(Style.Formal, Group(view, AppKind.WorkChat).Style);
        Assert.Equal(Style.Formal, store.Current.Style.WorkChat);
        Assert.Equal(Style.Casual, store.Current.Style.PersonalChat);
    }

    [Fact]
    public void AiPrompts_CannotBeGivenAStyle()
    {
        StyleSettingsService service = Service(out _);

        Assert.Null(Group(service.SetStyle(AppKind.AiPrompt, Style.Excited), AppKind.AiPrompt).Style);
    }

    [Fact]
    public void MovingAnApp_TakesItToItsNewCard_AndMovingItBack_ForgetsTheMove()
    {
        StyleSettingsService service = Service(out SettingsStore store);

        StyleView moved = service.MoveApp("Discord", AppKind.WorkChat);
        Assert.Contains(Group(moved, AppKind.WorkChat).Apps, a => a.Name == "Discord" && a.Moved);
        Assert.DoesNotContain(Group(moved, AppKind.PersonalChat).Apps, a => a.Name == "Discord");
        Assert.Equal(AppKind.WorkChat, store.Current.Style.AppKinds["Discord"]);

        StyleView back = service.MoveApp("discord", AppKind.PersonalChat);
        Assert.Contains(Group(back, AppKind.PersonalChat).Apps, a => a.Name == "Discord" && !a.Moved);
        Assert.Empty(store.Current.Style.AppKinds);
    }

    [Fact]
    public void AnUnlistedApp_CanBeAddedToACard_ByItsProcessName()
    {
        StyleSettingsService service = Service(out _);

        StyleView view = service.MoveApp("mattermost", AppKind.WorkChat);

        Assert.Contains(Group(view, AppKind.WorkChat).Apps, a => a is { Id: "mattermost", Name: "Mattermost", Moved: true });
        Assert.DoesNotContain(Group(service.MoveApp("mattermost", AppKind.Other), AppKind.WorkChat).Apps, a => a.Id == "mattermost");
    }

    [Fact]
    public void YourName_IsTrimmedAndKept()
    {
        StyleSettingsService service = Service(out SettingsStore store);

        Assert.Equal("Aadersh Kalyan", service.SetName("  Aadersh   Kalyan ").YourName);
        Assert.Equal("Aadersh Kalyan", store.Current.Style.YourName);
        Assert.Equal(StyleSettingsService.MaxNameLength, service.SetName(new string('a', 100)).YourName.Length);
    }

    [Fact]
    public void TheStyleSection_RoundTripsThroughSettingsJson()
    {
        string path = Path.Combine(Path.GetTempPath(), $"voicectrl-style-{Guid.NewGuid():N}.json");
        try
        {
            SettingsStore store = SettingsStore.Open(path, () => new UserSettings());
            var service = new StyleSettingsService(store, Ordinary.Contains);
            service.SetStyle(AppKind.Email, Style.VeryCasual);
            service.MoveApp("Slack", AppKind.PersonalChat);
            service.SetName("Sam");

            StyleSettings reread = SettingsStore.Open(path, () => new UserSettings()).Current.Style;

            Assert.Equal(Style.VeryCasual, reread.Email);
            Assert.Equal(AppKind.PersonalChat, reread.AppKinds["Slack"]);
            Assert.Equal("Sam", reread.YourName);
            Assert.Contains("\"veryCasual\"", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnOlderSettingsFile_GetsTheDefaultStyles()
    {
        string path = Path.Combine(Path.GetTempPath(), $"voicectrl-style-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "version": 1, "app": { "setupComplete": true } }""");

            StyleSettings styles = SettingsStore.Open(path, () => new UserSettings()).Current.Style;

            Assert.Equal(Style.Casual, styles.WorkChat);
            Assert.Equal(Style.Formal, styles.Email);
            Assert.Empty(styles.AppKinds);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
