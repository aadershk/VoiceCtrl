using VoiceCtrl.Core.Personalization;
using Xunit;

namespace VoiceCtrl.Core.Tests.Personalization;

public class AppProfileTableTests
{
    [Fact]
    public void ReadsEveryField()
    {
        AppProfileTable table = AppProfileTable.Parse("""
            {
              "slack": {
                "tone": "casual",
                "formatting": "prose",
                "cleanup": "light",
                "instructions": "keep it short"
              }
            }
            """);

        AppProfile profile = Assert.IsType<AppProfile>(table.Resolve("slack"));

        Assert.Equal("casual", profile.Tone);
        Assert.Equal("prose", profile.Formatting);
        Assert.Equal("light", profile.Cleanup);
        Assert.Equal("keep it short", profile.Instructions);
    }

    [Theory]
    [InlineData("slack")]
    [InlineData("slack.exe")]
    [InlineData("Slack.EXE")]
    public void ResolvesWithOrWithoutTheExeSuffix(string lookup)
    {
        // Task Manager shows "slack.exe" while the foreground window reports "slack", and the file
        // is written by hand from whichever the user happened to be looking at.
        AppProfileTable table = AppProfileTable.Parse("""{ "slack.exe": { "tone": "casual" } }""");

        Assert.NotNull(table.Resolve(lookup));
    }

    [Fact]
    public void FieldNamesAreCaseInsensitive()
    {
        AppProfileTable table = AppProfileTable.Parse("""{ "slack": { "Tone": "casual" } }""");

        Assert.Equal("casual", table.Resolve("slack")?.Tone);
    }

    [Fact]
    public void OmittedFieldsStayNull_SoTheyCanFallThroughToTheBuiltIns()
    {
        AppProfileTable table = AppProfileTable.Parse("""{ "slack": { "tone": "casual" } }""");
        AppProfile profile = Assert.IsType<AppProfile>(table.Resolve("slack"));

        Assert.Null(profile.Formatting);
        Assert.Null(profile.Cleanup);
        Assert.Null(profile.Instructions);
    }

    [Fact]
    public void UnknownApplication_ResolvesToNull()
    {
        AppProfileTable table = AppProfileTable.Parse("""{ "slack": { "tone": "casual" } }""");

        Assert.Null(table.Resolve("notepad"));
    }

    [Fact]
    public void NullProcessName_ResolvesToNull()
    {
        Assert.Null(AppProfileTable.Parse("""{ "slack": { "tone": "casual" } }""").Resolve(null));
    }

    [Fact]
    public void SeedFileParsesIntoItsTwoDocumentedEntries()
    {
        // The comment block at the top of the seed file is a JSON array, which would abort a
        // straight dictionary deserialization and silently discard the user's real entries.
        AppProfileTable table = AppProfileTable.Parse(AppProfileTable.SeedContents);

        Assert.Equal(2, table.Count);
        Assert.Equal("prose", table.Resolve("slack")?.Formatting);
        Assert.Equal("structured", table.Resolve("code")?.Formatting);
    }

    [Fact]
    public void EntryThatIsNotAnObject_IsSkippedWithoutTakingTheRestWithIt()
    {
        AppProfileTable table = AppProfileTable.Parse("""
            { "_comment": ["a note"], "slack": { "tone": "casual" } }
            """);

        Assert.Equal(1, table.Count);
        Assert.NotNull(table.Resolve("slack"));
    }

    [Fact]
    public void MalformedEntry_CostsOnlyThatEntry()
    {
        AppProfileTable table = AppProfileTable.Parse("""
            { "code": { "tone": 5 }, "slack": { "tone": "casual" } }
            """);

        Assert.Null(table.Resolve("code"));
        Assert.NotNull(table.Resolve("slack"));
    }

    [Fact]
    public void CommentsAndTrailingCommasAreTolerated()
    {
        AppProfileTable table = AppProfileTable.Parse("""
            {
              // hand-edited files pick these up from other config formats
              "slack": { "tone": "casual", },
            }
            """);

        Assert.NotNull(table.Resolve("slack"));
    }

    [Fact]
    public void InvalidJson_FallsBackToEmptyRatherThanFailingTheTranscription()
    {
        AppProfileTable table = AppProfileTable.Parse("{ this is not json");

        Assert.Equal(0, table.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyFile_IsEmpty(string contents)
    {
        Assert.Equal(0, AppProfileTable.Parse(contents).Count);
    }
}
