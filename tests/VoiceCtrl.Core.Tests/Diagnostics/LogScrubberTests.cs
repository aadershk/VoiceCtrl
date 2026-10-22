using VoiceCtrl.Core.Diagnostics;
using Xunit;

namespace VoiceCtrl.Core.Tests.Diagnostics;

public class LogScrubberTests
{
    private const string Profile = @"C:\Users\Aader";
    private const string GeminiKey = "AIzaSyD4f9Kq2LmN8pQrStUvWxYz0123456789ab";
    private const string TypeSafeKey = "ts_live_7hG2kP9qR4sT6vW8xY0zA1bC3dE5fH";

    private static LogScrubber Scrubber() => new(Profile, "Aader", [GeminiKey, TypeSafeKey, null, ""]);

    [Fact]
    public void Scrub_DropsTheSavedKeysWhereverTheyAppear()
    {
        string scrubbed = Scrubber().Scrub($"ERROR [Gemini] HttpRequestException: 400 for key={GeminiKey}&alt=json; typesafe {TypeSafeKey}");

        Assert.DoesNotContain(GeminiKey, scrubbed);
        Assert.DoesNotContain(TypeSafeKey, scrubbed);
        Assert.Contains("[key]", scrubbed);
    }

    [Theory]
    [InlineData("AIzaSyA0000000000000000000000000000000")]
    [InlineData("sk-ant-api03-abcdEFGHijklMNOPqrstUVWX0123456789")]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9abcdef1234567890ABCDEF")]
    [InlineData("8c3f9e7a2b1d4c6e8f0a2b4c6d8e0f1a3b5c7d9e")]
    public void Scrub_DropsAnythingShapedLikeAKey_EvenOneItWasNotTold(string secret)
    {
        string scrubbed = Scrubber().Scrub($"INFO  request with {secret} failed");

        Assert.DoesNotContain(secret.Split(' ')[^1], scrubbed);
        Assert.StartsWith("INFO  request with", scrubbed);
    }

    [Theory]
    [InlineData("INFO  Pasted \"Meet me at the Rijksmuseum at noon\" into Outlook", "\"…\"")]
    [InlineData("ERROR [Gemini] GeminiApiException: model said “please call Sam about the invoice”", "“…”")]
    public void Scrub_DropsQuotedText_SoNoDictationLeaves(string line, string placeholder)
    {
        string scrubbed = Scrubber().Scrub(line);

        Assert.DoesNotContain("Rijksmuseum", scrubbed);
        Assert.DoesNotContain("invoice", scrubbed);
        Assert.Contains(placeholder, scrubbed);
    }

    [Theory]
    [InlineData(@"ERROR [Profiles] IOException: C:\Users\Aader\AppData\Local\VoiceCtrl\profiles.json is locked")]
    [InlineData(@"ERROR [Profiles] IOException: c:\users\aader\AppData\Local\VoiceCtrl\profiles.json is locked")]
    [InlineData(@"INFO  json path C:\\Users\\Aader\\AppData\\Local\\VoiceCtrl")]
    [InlineData("INFO  forward C:/Users/Aader/AppData/Local/VoiceCtrl")]
    public void Scrub_ReplacesTheUserProfilePath(string line)
    {
        string scrubbed = Scrubber().Scrub(line);

        Assert.DoesNotContain("Aader", scrubbed, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", scrubbed);
        Assert.Contains("VoiceCtrl", scrubbed);
    }

    [Fact]
    public void Scrub_ReplacesAnotherAccountsFolderToo()
    {
        string scrubbed = Scrubber().Scrub(@"INFO  v3 .env found at D:\Users\someone.else\Downloads\VoiceCtrl");

        Assert.DoesNotContain("someone.else", scrubbed);
        Assert.Contains(@"%USERPROFILE%\Downloads\VoiceCtrl", scrubbed);
    }

    [Fact]
    public void Scrub_ReplacesTheUserNameAndEmailAddresses()
    {
        string scrubbed = Scrubber().Scrub("INFO  signed in as Aader on AADER-PC, mail aader@example.com");

        Assert.DoesNotContain("Aader", scrubbed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.com", scrubbed);
        Assert.Contains("%USERNAME%", scrubbed);
        Assert.Contains("[email]", scrubbed);
    }

    [Fact]
    public void Scrub_LeavesAnOrdinaryLogLineAlone()
    {
        const string line = "2026-09-30 09:41:02 INFO  Style detection: process=OUTLOOK tone=set formatting=none cleanup=standard dictionary=12";

        Assert.Equal(line, Scrubber().Scrub(line));
    }

    [Fact]
    public void ScrubValue_KeepsQuotes_ButStillDropsKeysAndPaths()
    {
        string scrubbed = Scrubber().ScrubValue($@"Headset ""Pro"" at C:\Users\Aader {GeminiKey}");

        Assert.Equal(@"Headset ""Pro"" at %USERPROFILE% [key]", scrubbed);
    }
}
