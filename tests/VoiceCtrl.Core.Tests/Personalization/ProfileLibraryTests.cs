using System.IO;
using VoiceCtrl.Core.Personalization;
using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Personalization;

public class ProfileLibraryTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"VoiceCtrlTests_{Path.GetRandomFileName()}");

    private readonly ProfileLibrary _library;

    public ProfileLibraryTests()
    {
        Directory.CreateDirectory(_directory);
        _library = new ProfileLibrary(ProfilesPath);
    }

    private string ProfilesPath => Path.Combine(_directory, "profiles.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private static ProfileEntry Outlook(
        string tone = "friendly but professional",
        string formatting = AppProfile.Prose,
        string cleanup = ProfileLibrary.DefaultCleanup,
        bool emailLayout = true) =>
        new("outlook", "Outlook", tone, formatting, cleanup, emailLayout, Instructions: string.Empty);

    private AppProfile? Stored(string processName) => AppProfileTable.Parse(File.ReadAllText(ProfilesPath)).Resolve(processName);

    // ---- Reading

    [Fact]
    public void NoFile_MeansNoProfiles()
    {
        Assert.Empty(_library.Profiles());
    }

    [Fact]
    public void TheSeedFile_ListsItsTwoProfiles_ByAppName()
    {
        File.WriteAllText(ProfilesPath, AppProfileTable.SeedContents);

        IReadOnlyList<ProfileEntry> profiles = _library.Profiles();

        Assert.Equal(["Slack", "Visual Studio Code"], profiles.Select(p => p.AppName));
        Assert.Equal(ProfileLibrary.DefaultCleanup, profiles[0].Cleanup);
        Assert.False(profiles[0].EmailLayout);
    }

    [Fact]
    public void AHandWrittenProfileWithNoAppName_ShowsItsProcessName()
    {
        File.WriteAllText(ProfilesPath, """{ "notepad": { "tone": "plain" } }""");

        Assert.Equal("Notepad", Assert.Single(_library.Profiles()).AppName);
    }

    [Theory]
    [InlineData("slack", AppProfile.Prose)]
    [InlineData("code", AppProfile.Structured)]
    [InlineData("winword", AppProfile.Paragraphs)]
    [InlineData("notepad", AppProfile.None)]
    public void AnUnsetFormatting_ShowsWhatTheAppGetsAnyway(string processName, string expected)
    {
        File.WriteAllText(ProfilesPath, $$"""{ "{{processName}}": { "tone": "plain" } }""");

        Assert.Equal(expected, Assert.Single(_library.Profiles()).Formatting);
    }

    [Fact]
    public void AnUnknownCleanup_ShowsAsDefault_SinceThatIsWhatTranscriptionUses()
    {
        File.WriteAllText(ProfilesPath, """{ "notepad": { "cleanup": "thorough" } }""");

        Assert.Equal(ProfileLibrary.DefaultCleanup, Assert.Single(_library.Profiles()).Cleanup);
    }

    // ---- Saving

    [Fact]
    public void Save_WritesEveryField_TheWayTranscriptionReadsThem()
    {
        Assert.Equal(ProfileEditError.None, _library.Save(original: null, Outlook(cleanup: CleanupLevels.Verbatim)));

        AppProfile stored = Stored("outlook")!;
        Assert.Equal("Outlook", stored.App);
        Assert.Equal("friendly but professional", stored.Tone);
        Assert.Equal(AppProfile.Prose, stored.Formatting);
        Assert.Equal(CleanupLevels.Verbatim, stored.Cleanup);
        Assert.True(GeminiPrompt.ContextFor(stored, "outlook", new PromptDefaults(CleanupLevels.Standard, true, [], [])).EmailLayout);
    }

    [Fact]
    public void DefaultCleanup_AndEmailLayoutAsTheAppGetsIt_AreLeftOutOfTheFile()
    {
        _library.Save(original: null, Outlook(emailLayout: true));
        _library.Save(original: null, Outlook(emailLayout: false) with { ProcessName = "notepad", AppName = "Notepad" });

        string json = File.ReadAllText(ProfilesPath);
        Assert.DoesNotContain("cleanup", json);
        Assert.DoesNotContain("emailLayout", json);
        Assert.Null(Stored("outlook")!.Cleanup);
    }

    // ---- Ticket 55: email apps are laid out as emails by default

    [Fact]
    public void AnUnsetEmailLayout_ShowsOnForAnEmailApp_AndOffForAnother()
    {
        File.WriteAllText(ProfilesPath, """{ "olk": { "tone": "plain" }, "notepad": { "tone": "plain" } }""");

        IReadOnlyList<ProfileEntry> profiles = _library.Profiles();

        Assert.True(profiles.Single(p => p.ProcessName == "olk").EmailLayout);
        Assert.False(profiles.Single(p => p.ProcessName == "notepad").EmailLayout);
    }

    [Fact]
    public void TurningEmailLayoutOffForAnEmailApp_IsWrittenDown()
    {
        _library.Save(original: null, Outlook(emailLayout: false));

        Assert.False(Stored("outlook")!.EmailLayout);
        Assert.False(Assert.Single(_library.Profiles()).EmailLayout);
    }

    [Fact]
    public void TurningEmailLayoutOnForAnotherApp_IsWrittenDown()
    {
        _library.Save(original: null, Outlook(emailLayout: true) with { ProcessName = "notepad", AppName = "Notepad" });

        Assert.True(Stored("notepad")!.EmailLayout);
    }

    [Fact]
    public void ANewProfile_StartsWithWhatTheAppGetsToday()
    {
        IReadOnlyList<RunningApp> apps = ProfileLibrary.RunningApps(
            [new AppWindow("olk", "Outlook"), new AppWindow("WINWORD", "Microsoft Word"), new AppWindow("notepad", null)], []);

        RunningApp outlook = apps.Single(a => a.ProcessName == "olk");
        Assert.True(outlook.EmailLayout);
        Assert.Equal(AppProfile.None, outlook.Formatting);
        Assert.Equal(AppProfile.Paragraphs, apps.Single(a => a.ProcessName == "WINWORD").Formatting);
        Assert.False(apps.Single(a => a.ProcessName == "notepad").EmailLayout);
    }

    [Fact]
    public void Paragraphs_IsAFormattingAProfileCanSave()
    {
        Assert.Equal(ProfileEditError.None, _library.Save(original: null, Outlook(formatting: AppProfile.Paragraphs)));

        Assert.Equal(AppProfile.Paragraphs, Stored("outlook")!.Formatting);
    }

    [Fact]
    public void AnEmptyTone_IsLeftUnset_SoTheBuiltInToneStillApplies()
    {
        _library.Save(original: null, Outlook(tone: "   "));

        Assert.Null(Stored("outlook")!.Tone);
    }

    [Fact]
    public void Save_KeepsTheFilesCommentAndOtherProfiles()
    {
        File.WriteAllText(ProfilesPath, AppProfileTable.SeedContents);

        _library.Save(original: null, Outlook());

        string json = File.ReadAllText(ProfilesPath);
        Assert.Contains("_comment", json);
        Assert.Equal(["Outlook", "Slack", "Visual Studio Code"], _library.Profiles().Select(p => p.AppName));
    }

    [Fact]
    public void Save_KeepsInstructionsTheHubDoesNotShow()
    {
        File.WriteAllText(ProfilesPath, """{ "outlook": { "app": "Outlook", "instructions": "UK spelling" } }""");
        ProfileEntry shown = Assert.Single(_library.Profiles());

        _library.Save("outlook", shown with { Tone = "formal" });

        Assert.Equal("UK spelling", Stored("outlook")!.Instructions);
    }

    [Fact]
    public void ASecondProfileForTheSameApp_IsRefused()
    {
        _library.Save(original: null, Outlook());

        Assert.Equal(ProfileEditError.AlreadyThere, _library.Save(original: null, Outlook(tone: "casual")));
        Assert.Equal("friendly but professional", Stored("outlook")!.Tone);
    }

    [Fact]
    public void ChangingTheApp_MovesTheProfile()
    {
        _library.Save(original: null, Outlook());

        _library.Save("outlook", Outlook() with { ProcessName = "thunderbird.exe", AppName = "Thunderbird" });

        Assert.Equal("thunderbird", Assert.Single(_library.Profiles()).ProcessName);
    }

    [Fact]
    public void ChangingTheApp_ToOneThatHasAProfile_IsRefused()
    {
        _library.Save(original: null, Outlook());
        _library.Save(original: null, Outlook() with { ProcessName = "slack", AppName = "Slack" });

        Assert.Equal(ProfileEditError.AlreadyThere, _library.Save("outlook", Outlook() with { ProcessName = "slack", AppName = "Slack" }));
        Assert.Equal(2, _library.Profiles().Count);
    }

    [Theory]
    [InlineData("", ProfileEditError.AppMissing)]
    [InlineData("  .exe ", ProfileEditError.AppMissing)]
    [InlineData(@"C:\apps\outlook", ProfileEditError.AppMissing)]
    public void AProfileNeedsAnApp(string processName, ProfileEditError expected)
    {
        Assert.Equal(expected, _library.Save(original: null, Outlook() with { ProcessName = processName }));
        Assert.False(File.Exists(ProfilesPath));
    }

    [Fact]
    public void ALongTone_IsRefused()
    {
        Assert.Equal(ProfileEditError.ToneTooLong, _library.Save(original: null, Outlook(tone: new string('a', ProfileLibrary.MaxToneLength + 1))));
    }

    [Theory]
    [InlineData("bulleted", CleanupLevels.Light, ProfileEditError.FormattingInvalid)]
    [InlineData(AppProfile.Prose, "thorough", ProfileEditError.CleanupInvalid)]
    public void OnlyTheEditorsChoices_AreSaved(string formatting, string cleanup, ProfileEditError expected)
    {
        Assert.Equal(expected, _library.Save(original: null, Outlook(formatting: formatting, cleanup: cleanup)));
    }

    [Fact]
    public void AToneWithLineBreaks_IsSavedOnOneLine()
    {
        _library.Save(original: null, Outlook(tone: "formal\r\nno contractions"));

        Assert.Equal("formal no contractions", Stored("outlook")!.Tone);
    }

    // ---- Removing and Undo

    [Fact]
    public void Remove_ThenRestore_PutsTheProfileBackAsItWas()
    {
        File.WriteAllText(ProfilesPath, """{ "outlook": { "app": "Outlook", "tone": "formal", "emailLayout": true, "instructions": "UK spelling" } }""");
        ProfileEntry removed = Assert.Single(_library.Profiles());

        Assert.True(_library.Remove("outlook.exe"));
        Assert.Empty(_library.Profiles());

        _library.Restore(removed);

        Assert.Equal(removed, Assert.Single(_library.Profiles()));
        Assert.Equal("UK spelling", Stored("outlook")!.Instructions);
    }

    [Fact]
    public void Remove_OfAProfileThatIsGone_ChangesNothing()
    {
        Assert.False(_library.Remove("outlook"));
    }

    [Fact]
    public void Restore_NeverReplacesAProfileMadeSince()
    {
        _library.Save(original: null, Outlook());
        ProfileEntry removed = _library.Profiles()[0];
        _library.Remove("outlook");
        _library.Save(original: null, Outlook(tone: "casual"));

        _library.Restore(removed);

        Assert.Equal("casual", Stored("outlook")!.Tone);
    }

    // ---- Running apps

    [Fact]
    public void RunningApps_AreOnePerApp_ByName_WithoutVoiceCtrlOrTheShell()
    {
        IReadOnlyList<RunningApp> apps = ProfileLibrary.RunningApps(
            [
                new("OUTLOOK", "Microsoft Outlook"),
                new("slack", "Slack"),
                new("slack", "Slack"),
                new("explorer", "Windows Explorer"),
                new("VoiceCtrl", "VoiceCtrl"),
                new("msedgewebview2", "Microsoft Edge WebView2"),
                new("notepad", null),
            ],
            profiles: []);

        Assert.Equal(["Microsoft Outlook", "Notepad", "Slack"], apps.Select(a => a.AppName));
    }

    [Fact]
    public void RunningApps_SayWhichAlreadyHaveAProfile_AndWhatFormattingTheyGetNow()
    {
        IReadOnlyList<RunningApp> apps = ProfileLibrary.RunningApps(
            [new("slack", "Slack"), new("Code", "Visual Studio Code")],
            profiles: [Outlook() with { ProcessName = "slack" }]);

        RunningApp slack = apps.Single(a => a.ProcessName == "slack");
        RunningApp code = apps.Single(a => a.ProcessName == "Code");
        Assert.True(slack.HasProfile);
        Assert.False(code.HasProfile);
        Assert.Equal(AppProfile.Structured, code.Formatting);
    }

    // ---- Style and How I write here (ticket 56)

    [Fact]
    public void AnOldToneText_ShowsAsHowIWriteHere_AndMovesThereOnSave()
    {
        File.WriteAllText(ProfilesPath, """{ "slack": { "tone": "short and friendly" } }""");

        ProfileEntry entry = Assert.Single(_library.Profiles());
        Assert.Equal("short and friendly", entry.Instructions);
        Assert.Equal(string.Empty, entry.Tone);

        Assert.Equal(ProfileEditError.None, _library.Save("slack", entry));
        AppProfile stored = Stored("slack")!;
        Assert.Equal("short and friendly", stored.Instructions);
        Assert.Null(stored.Tone);
    }

    [Fact]
    public void AToneOfNone_StaysASetting()
    {
        File.WriteAllText(ProfilesPath, """{ "slack": { "tone": "none" } }""");

        ProfileEntry entry = Assert.Single(_library.Profiles());

        Assert.Equal("none", entry.Tone);
        Assert.Equal(string.Empty, entry.Instructions);
    }

    [Fact]
    public void AProfilesStyle_IsSaved_AndSameAsItsKindStoresNothing()
    {
        Assert.Equal(ProfileEditError.None, _library.Save(null, Outlook(tone: "") with { Style = "veryCasual" }));
        Assert.Equal("veryCasual", Stored("outlook")!.Style);
        Assert.Equal("veryCasual", _library.Profiles()[0].Style);

        Assert.Equal(ProfileEditError.None, _library.Save("outlook", Outlook(tone: "") with { Style = "" }));
        Assert.Null(Stored("outlook")!.Style);
    }

    [Fact]
    public void AnUnknownStyle_IsRefused()
    {
        Assert.Equal(ProfileEditError.StyleInvalid, _library.Save(null, Outlook() with { Style = "shouty" }));
    }

    [Fact]
    public void HowIWriteHere_IsKeptShort()
    {
        Assert.Equal(ProfileEditError.InstructionsTooLong,
            _library.Save(null, Outlook(tone: "") with { Instructions = new string('x', ProfileLibrary.MaxInstructionsLength + 1) }));
    }

    [Fact]
    public void EachProfile_KnowsItsAppsKind_AfterAMoveOnTheStyleScreen()
    {
        var moved = new Dictionary<string, AppKind> { ["Discord"] = AppKind.WorkChat };
        var library = new ProfileLibrary(ProfilesPath, () => moved);
        File.WriteAllText(ProfilesPath, """{ "discord": {}, "outlook": {} }""");

        Assert.Equal([AppKind.WorkChat, AppKind.Email], library.Profiles().Select(p => p.Kind));
    }
}
