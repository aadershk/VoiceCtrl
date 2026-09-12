using VoiceCtrl.Core.Config;
using Xunit;

namespace VoiceCtrl.Core.Tests.Config;

public class EnvFileEditorTests
{
    [Fact]
    public void ReplacesAMatchingKeyInPlace()
    {
        string[] result = EnvFileEditor.ApplyUpdates(
            ["GEMINI_API_KEY=old-key", "TRANSCRIPTION_MODE=Online"],
            new Dictionary<string, string> { ["GEMINI_API_KEY"] = "new-key" });

        Assert.Equal(["GEMINI_API_KEY=new-key", "TRANSCRIPTION_MODE=Online"], result);
    }

    [Fact]
    public void PreservesCommentsBlankLinesAndUnrelatedKeys()
    {
        string[] lines =
        [
            "# VoiceCtrl configuration",
            "GEMINI_API_KEY=old-key",
            "",
            "# Optional overrides",
            "CLEANUP_LEVEL=standard",
        ];

        string[] result = EnvFileEditor.ApplyUpdates(lines, new Dictionary<string, string> { ["GEMINI_API_KEY"] = "new-key" });

        Assert.Equal(
        [
            "# VoiceCtrl configuration",
            "GEMINI_API_KEY=new-key",
            "",
            "# Optional overrides",
            "CLEANUP_LEVEL=standard",
        ], result);
    }

    [Fact]
    public void MatchesKeysCaseInsensitively()
    {
        string[] result = EnvFileEditor.ApplyUpdates(
            ["gemini_api_key=old-key"],
            new Dictionary<string, string> { ["GEMINI_API_KEY"] = "new-key" });

        Assert.Equal(["gemini_api_key=new-key"], result);
    }

    [Fact]
    public void IgnoresAKeyLikeAssignmentInsideAComment()
    {
        string[] result = EnvFileEditor.ApplyUpdates(
            ["# GEMINI_API_KEY=placeholder", "GEMINI_API_KEY=old-key"],
            new Dictionary<string, string> { ["GEMINI_API_KEY"] = "new-key" });

        Assert.Equal(["# GEMINI_API_KEY=placeholder", "GEMINI_API_KEY=new-key"], result);
    }

    [Fact]
    public void AppendsAMissingKeyAtTheEnd()
    {
        string[] result = EnvFileEditor.ApplyUpdates(
            ["CLEANUP_LEVEL=standard"],
            new Dictionary<string, string> { ["GEMINI_API_KEY"] = "new-key" });

        Assert.Equal(["CLEANUP_LEVEL=standard", "GEMINI_API_KEY=new-key"], result);
    }

    [Fact]
    public void UpdatesMultipleKeysInOnePass()
    {
        string[] result = EnvFileEditor.ApplyUpdates(
            ["GEMINI_API_KEY=old-key", "TRANSCRIPTION_MODE=Online"],
            new Dictionary<string, string> { ["GEMINI_API_KEY"] = "new-key", ["TRANSCRIPTION_MODE"] = "Offline" });

        Assert.Equal(["GEMINI_API_KEY=new-key", "TRANSCRIPTION_MODE=Offline"], result);
    }

    [Fact]
    public void CanWriteAnEmptyValue()
    {
        string[] result = EnvFileEditor.ApplyUpdates(
            ["GEMINI_API_KEY=old-key"],
            new Dictionary<string, string> { ["GEMINI_API_KEY"] = "" });

        Assert.Equal(["GEMINI_API_KEY="], result);
    }
}
