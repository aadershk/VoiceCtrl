using System.IO;
using System.Text;
using VoiceCtrl.Core.Config;
using Xunit;

namespace VoiceCtrl.Core.Tests.Config;

public sealed class SecretsStoreTests : IDisposable
{
    private const string GeminiKey = "AIzaSyTestGeminiKey000000000000000000";
    private const string TypeSafeKey = "ts_test_typesafe_key_123";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"voicectrl-test-secrets-{Guid.NewGuid()}");

    private string SecretsPath => Path.Combine(_dir, "secrets.bin");

    public SecretsStoreTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void NoFile_BothKeysAreEmpty()
    {
        SecretsStore secrets = SecretsStore.Load(SecretsPath);

        Assert.Equal(string.Empty, secrets.GeminiApiKey);
        Assert.Equal(string.Empty, secrets.TypeSafeApiKey);
        Assert.False(File.Exists(SecretsPath));
    }

    [Fact]
    public void BothKeys_SurviveAReload()
    {
        SecretsStore secrets = SecretsStore.Load(SecretsPath);

        secrets.SetGeminiApiKey(GeminiKey);
        secrets.SetTypeSafeApiKey(TypeSafeKey);
        SecretsStore reloaded = SecretsStore.Load(SecretsPath);

        Assert.Equal(GeminiKey, reloaded.GeminiApiKey);
        Assert.Equal(TypeSafeKey, reloaded.TypeSafeApiKey);
    }

    [Fact]
    public void TheFileOnDisk_NeverContainsAKeyInPlainText()
    {
        SecretsStore secrets = SecretsStore.Load(SecretsPath);

        secrets.SetGeminiApiKey(GeminiKey);

        byte[] onDisk = File.ReadAllBytes(SecretsPath);
        Assert.DoesNotContain(GeminiKey, Encoding.UTF8.GetString(onDisk), StringComparison.Ordinal);
        Assert.DoesNotContain(GeminiKey, Encoding.Unicode.GetString(onDisk), StringComparison.Ordinal);
    }

    [Fact]
    public void Keys_AreTrimmed()
    {
        SecretsStore secrets = SecretsStore.Load(SecretsPath);

        secrets.SetGeminiApiKey($"  {GeminiKey}\r\n");

        Assert.Equal(GeminiKey, secrets.GeminiApiKey);
    }

    [Fact]
    public void AFileThatCannotBeDecrypted_IsMovedAsideAndTheKeysStartEmpty()
    {
        File.WriteAllBytes(SecretsPath, [1, 2, 3, 4, 5]);

        SecretsStore secrets = SecretsStore.Load(SecretsPath);

        Assert.Equal(string.Empty, secrets.GeminiApiKey);
        Assert.Single(Directory.GetFiles(_dir, "secrets.unreadable-*.bin"));

        secrets.SetGeminiApiKey(GeminiKey);
        Assert.Equal(GeminiKey, SecretsStore.Load(SecretsPath).GeminiApiKey);
    }

    [Fact]
    public void Changed_IsRaisedOnlyWhenAKeyActuallyChanges()
    {
        SecretsStore secrets = SecretsStore.Load(SecretsPath);
        int raised = 0;
        secrets.Changed += () => raised++;

        secrets.SetGeminiApiKey(GeminiKey);
        secrets.SetGeminiApiKey(GeminiKey);
        secrets.SetTypeSafeApiKey(TypeSafeKey);

        Assert.Equal(2, raised);
    }

    [Fact]
    public void ToString_NeverRevealsAKey()
    {
        SecretsStore secrets = SecretsStore.InMemory();
        secrets.SetGeminiApiKey(GeminiKey);

        Assert.DoesNotContain(GeminiKey, secrets.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
