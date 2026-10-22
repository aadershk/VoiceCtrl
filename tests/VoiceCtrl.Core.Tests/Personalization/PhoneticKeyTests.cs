using VoiceCtrl.Core.Personalization;
using Xunit;

namespace VoiceCtrl.Core.Tests.Personalization;

public class PhoneticKeyTests
{
    [Theory]
    [InlineData("Schiphol", "Skiphol")]
    [InlineData("Phoebe", "Phebe")]
    [InlineData("EconML", "econ ML")]
    [InlineData("XGBoost", "XG Boost")]
    [InlineData("Kubernetes", "kuber netes")]
    public void SoundAlikes_ShareAKey(string a, string b)
    {
        Assert.Equal(PhoneticKey.Encode(a), PhoneticKey.Encode(b));
    }

    [Theory]
    [InlineData("Grafana", "Prometheus")]
    [InlineData("Schiphol", "Haarlem")]
    public void UnrelatedWords_DoNotShareAKey(string a, string b)
    {
        Assert.NotEqual(PhoneticKey.Encode(a), PhoneticKey.Encode(b));
    }

    [Fact]
    public void PunctuationDigitsAndSpaces_AreIgnored()
    {
        Assert.Equal(PhoneticKey.Encode("scikit-learn"), PhoneticKey.Encode("scikit learn"));
        Assert.Equal(string.Empty, PhoneticKey.Encode("123 !?"));
    }

    [Fact]
    public void Distance_CountsEdits()
    {
        Assert.Equal(0, PhoneticKey.Distance("KRF", "KRF"));
        Assert.Equal(1, PhoneticKey.Distance("KRF", "KRFN"));
        Assert.Equal(3, PhoneticKey.Distance("", "KRF"));
    }
}
