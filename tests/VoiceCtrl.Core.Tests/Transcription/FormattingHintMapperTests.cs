using VoiceCtrl.Core.Transcription;
using Xunit;

namespace VoiceCtrl.Core.Tests.Transcription;

public class FormattingHintMapperTests
{
    [Fact]
    public void KnownStructuredApp_ResolvesToStructuredHint()
    {
        string? result = FormattingHintMapper.Resolve("windowsterminal");
        Assert.Contains("Markdown", result);
    }

    [Fact]
    public void KnownProseApp_ResolvesToProseHint()
    {
        string? result = FormattingHintMapper.Resolve("whatsapp");
        Assert.Contains("continuous natural sentences", result);
    }

    [Fact]
    public void IsCaseInsensitive()
    {
        Assert.Equal(FormattingHintMapper.Resolve("whatsapp"), FormattingHintMapper.Resolve("WhatsApp"));
    }

    [Fact]
    public void UnknownProcessName_ReturnsNull()
    {
        Assert.Null(FormattingHintMapper.Resolve("some_random_app"));
    }

    [Fact]
    public void NullProcessName_ReturnsNull()
    {
        Assert.Null(FormattingHintMapper.Resolve(null));
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [InlineData("cmd")]
    [InlineData("conhost")]
    [InlineData("mintty")]
    [InlineData("cursor")]
    public void CommonTerminalHosts_ResolveToStructuredHint(string processName)
    {
        // Claude Code (and most CLI/AI tools) run inside one of these hosts, and GetForegroundWindow
        // resolves to the host, not to the CLI process itself, so these need built-in coverage.
        string? result = FormattingHintMapper.Resolve(processName);
        Assert.Contains("Markdown", result);
    }

    [Fact]
    public void ExtraStructuredApps_ResolvesToStructuredHint()
    {
        string? result = FormattingHintMapper.Resolve("cursor", extraStructuredApps: new[] { "cursor" });
        Assert.Contains("Markdown", result);
    }

    [Fact]
    public void ExtraProseApps_ResolvesToProseHint()
    {
        string? result = FormattingHintMapper.Resolve("telegram", extraProseApps: new[] { "telegram" });
        Assert.Contains("continuous natural sentences", result);
    }

    [Fact]
    public void ExtraOverride_TakesPriorityOverBuiltInTable()
    {
        // "windowsterminal" is structured by default, and an explicit override should still win.
        string? result = FormattingHintMapper.Resolve("windowsterminal", extraProseApps: new[] { "windowsterminal" });
        Assert.Contains("continuous natural sentences", result);
    }
}
