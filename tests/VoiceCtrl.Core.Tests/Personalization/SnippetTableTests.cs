using VoiceCtrl.Core.Personalization;
using Xunit;

namespace VoiceCtrl.Core.Tests.Personalization;

public class SnippetTableTests
{
    [Fact]
    public void ExpandsATriggerToItsExpansion()
    {
        SnippetTable table = SnippetTable.Parse(["brb = be right back"]);
        Assert.Equal("ok, be right back", table.Expand("ok, brb"));
    }

    [Fact]
    public void TriggerMatchingIgnoresCase_AndTheExpansionKeepsItsOwn()
    {
        SnippetTable table = SnippetTable.Parse(["my work email = Firstname.Lastname@example.com"]);
        Assert.Equal("send it to Firstname.Lastname@example.com", table.Expand("send it to My Work Email"));
    }

    [Fact]
    public void LongestTriggerWinsAtTheSamePosition()
    {
        // .NET alternation is leftmost-first, not leftmost-longest, so this only holds because the
        // branches are ordered by descending length when the pattern is built.
        SnippetTable table = SnippetTable.Parse(["my work = the office", "my work email = a@b.com"]);
        Assert.Equal("send it to a@b.com", table.Expand("send it to my work email"));
    }

    [Fact]
    public void TriggersFireOnWholeWordsOnly()
    {
        SnippetTable table = SnippetTable.Parse(["brb = be right back"]);

        Assert.Equal("brbx and abrb", table.Expand("brbx and abrb"));
    }

    [Fact]
    public void ExpansionsAreNotRescanned()
    {
        // A single left-to-right pass is what makes a pair of snippets that name each other
        // terminate instead of looping.
        SnippetTable table = SnippetTable.Parse(["alpha = beta", "beta = gamma"]);
        Assert.Equal("beta", table.Expand("alpha"));
    }

    [Fact]
    public void EscapeSequenceBecomesALineBreak()
    {
        SnippetTable table = SnippetTable.Parse([@"signoff = Kind regards,\nAadersh"]);
        Assert.Equal("Kind regards,\nAadersh", table.Expand("signoff"));
    }

    [Fact]
    public void TriggerPunctuationIsMatchedLiterally()
    {
        SnippetTable table = SnippetTable.Parse(["e.g. = for example"]);

        Assert.Equal("for example this one", table.Expand("e.g. this one"));
        Assert.Equal("erg? this one", table.Expand("erg? this one"));
    }

    [Fact]
    public void EmptyExpansionDeletesTheTrigger()
    {
        // Deleting a spoken verbal tic outright is a legitimate use, so an empty right-hand side
        // is kept rather than treated as a malformed line.
        SnippetTable table = SnippetTable.Parse(["you know = "]);
        Assert.DoesNotContain("you know", table.Expand("so you know we ship"));
    }

    [Fact]
    public void SkipsCommentsBlanksAndLinesWithoutASeparator()
    {
        SnippetTable table = SnippetTable.Parse(["# a comment", "", "no separator here", "brb = be right back"]);
        Assert.Single(table.Snippets);
    }

    [Fact]
    public void RejectsAnEmptyTrigger()
    {
        // An empty trigger would match at every position in the transcript.
        Assert.Empty(SnippetTable.Parse(["= something"]).Snippets);
    }

    [Fact]
    public void DuplicateTrigger_KeepsTheFirstDefinition()
    {
        SnippetTable table = SnippetTable.Parse(["brb = be right back", "BRB = something else"]);

        Assert.Single(table.Snippets);
        Assert.Equal("be right back", table.Expand("brb"));
    }

    [Fact]
    public void StopsAtTheSnippetLimit()
    {
        IEnumerable<string> lines = Enumerable.Range(0, SnippetTable.MaxSnippets + 50)
            .Select(i => $"trigger{i} = expansion{i}");

        Assert.Equal(SnippetTable.MaxSnippets, SnippetTable.Parse(lines).Snippets.Count);
    }

    [Fact]
    public void SeedFileDefinesNoSnippets()
    {
        Assert.Empty(SnippetTable.Parse(SnippetTable.SeedContents.Split('\n')).Snippets);
    }

    [Fact]
    public void EmptyTable_ReturnsTextUnchanged()
    {
        Assert.Equal("nothing to expand", SnippetTable.Empty.Expand("nothing to expand"));
    }

    [Fact]
    public void EmptyText_IsHandled()
    {
        SnippetTable table = SnippetTable.Parse(["brb = be right back"]);
        Assert.Equal(string.Empty, table.Expand(string.Empty));
    }

    [Fact]
    public void ExtractHeader_StopsAtFirstSnippetLine()
    {
        string header = SnippetTable.ExtractHeader("# a comment\n\nbrb = be right back");
        Assert.Equal("# a comment\n\n", header);
    }

    [Fact]
    public void ExtractHeader_EntireSeedFileIsHeader()
    {
        // Every line in the shipped seed is commented out, so a Hub save on a never-touched file
        // must carry the whole thing forward rather than truncating it.
        string header = SnippetTable.ExtractHeader(SnippetTable.SeedContents);
        string normalizedSeed = SnippetTable.SeedContents.Replace("\r\n", "\n").TrimEnd('\n') + "\n";
        Assert.Equal(normalizedSeed, header);
    }

    [Fact]
    public void ExtractHeader_NoLeadingComments_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SnippetTable.ExtractHeader("brb = be right back"));
    }

    [Fact]
    public void Format_RoundTripsThroughParse_IncludingALineBreakInTheExpansion()
    {
        string header = SnippetTable.ExtractHeader(SnippetTable.SeedContents);
        KeyValuePair<string, string>[] snippets =
        [
            new("brb", "be right back"),
            new("signoff", "Kind regards,\nAadersh"),
        ];

        string formatted = SnippetTable.Format(header, snippets);

        Assert.Equal(header, SnippetTable.ExtractHeader(formatted));
        Assert.Equal(snippets, SnippetTable.Parse(formatted.Split('\n')).Snippets.ToArray());
    }

    [Fact]
    public void Format_NoSnippets_OmitsTrailingNewlineAfterHeader()
    {
        Assert.Equal("# header\n", SnippetTable.Format("# header\n", []));
    }
}
