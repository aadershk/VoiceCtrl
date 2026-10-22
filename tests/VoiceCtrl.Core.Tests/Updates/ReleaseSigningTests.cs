using System.Text;
using VoiceCtrl.Core.Updates;

namespace VoiceCtrl.Core.Tests.Updates;

public sealed class ReleaseSigningTests : IDisposable
{
    private static readonly (string Private, string Public) Keys = ReleaseSigning.CreateKeyPair();

    private readonly string _folder = Directory.CreateTempSubdirectory("voicectrl-signing-").FullName;
    private readonly string _package;

    public ReleaseSigningTests()
    {
        _package = Path.Combine(_folder, "VoiceCtrlApp-4.1.0-full.nupkg");
        File.WriteAllBytes(_package, Encoding.UTF8.GetBytes("the package bytes"));
    }

    private ReleaseManifest ManifestFor(string version = "4.1.0") =>
        new(ReleaseSigning.PackageId, version, Path.GetFileName(_package), ReleaseSigning.Sha256Of(_package));

    private static byte[] PublicKey => Convert.FromBase64String(Keys.Public);

    [Fact]
    public void ASignedManifest_Verifies_AndReadsBack()
    {
        byte[] signed = ReleaseSigning.Sign(ManifestFor(), Keys.Private);

        ReleaseManifest read = ReleaseSigning.Verify(signed, PublicKey, "4.1.0");

        Assert.Equal(ManifestFor(), read);
    }

    [Fact]
    public void AManifestSignedWithAnotherKey_IsRefused()
    {
        (string otherPrivate, _) = ReleaseSigning.CreateKeyPair();
        byte[] signed = ReleaseSigning.Sign(ManifestFor(), otherPrivate);

        Assert.Throws<UpdateVerificationException>(() => ReleaseSigning.Verify(signed, PublicKey, "4.1.0"));
    }

    [Fact]
    public void AnEditedManifest_IsRefused()
    {
        byte[] signed = ReleaseSigning.Sign(ManifestFor(), Keys.Private);
        string json = Encoding.UTF8.GetString(signed);
        // Swap the signed payload for another one, keeping the old signature.
        byte[] otherPayload = ReleaseSigning.Sign(ManifestFor() with { Sha256 = new string('0', 64) }, Keys.Private);
        string forged = ReplacePayload(json, PayloadOf(Encoding.UTF8.GetString(otherPayload)));

        Assert.Throws<UpdateVerificationException>(() => ReleaseSigning.Verify(Encoding.UTF8.GetBytes(forged), PublicKey, "4.1.0"));
    }

    [Fact]
    public void AManifestForAnotherVersion_IsRefused()
    {
        // A genuine old manifest replayed next to a newer package.
        byte[] signed = ReleaseSigning.Sign(ManifestFor("4.0.1"), Keys.Private);

        Assert.Throws<UpdateVerificationException>(() => ReleaseSigning.Verify(signed, PublicKey, "4.1.0"));
    }

    [Fact]
    public void AManifestForAnotherApp_IsRefused()
    {
        byte[] signed = ReleaseSigning.Sign(ManifestFor() with { PackageId = "SomethingElse" }, Keys.Private);

        Assert.Throws<UpdateVerificationException>(() => ReleaseSigning.Verify(signed, PublicKey, "4.1.0"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"payload\":\"%%%\",\"signature\":\"AAAA\"}")]
    public void AMalformedManifest_IsRefused(string text)
    {
        Assert.Throws<UpdateVerificationException>(() => ReleaseSigning.Verify(Encoding.UTF8.GetBytes(text), PublicKey, "4.1.0"));
    }

    [Fact]
    public void ThePackageTheManifestDescribes_Passes()
    {
        ReleaseSigning.VerifyPackage(ManifestFor(), _package);
    }

    [Fact]
    public void AChangedPackage_IsRefused()
    {
        ReleaseManifest manifest = ManifestFor();
        File.AppendAllText(_package, "and a little more");

        Assert.Throws<UpdateVerificationException>(() => ReleaseSigning.VerifyPackage(manifest, _package));
    }

    [Fact]
    public void AMissingPackage_IsRefused()
    {
        ReleaseManifest manifest = ManifestFor();
        File.Delete(_package);

        Assert.Throws<UpdateVerificationException>(() => ReleaseSigning.VerifyPackage(manifest, _package));
    }

    [Fact]
    public void TheCompiledInKey_IsAnEd25519PublicKey()
    {
        Assert.Equal(32, ReleaseSigning.PublicKey.Length);
    }

    [Fact]
    public void TheManifestFileName_CarriesTheVersion_SoOneFeedFolderCanHoldSeveral()
    {
        Assert.Equal("VoiceCtrlApp-4.1.0-manifest.json", ReleaseSigning.ManifestFileName("4.1.0"));
    }

    private static string PayloadOf(string json) =>
        System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("payload").GetString()!;

    private static string ReplacePayload(string json, string payload) =>
        json.Replace(PayloadOf(json), payload, StringComparison.Ordinal);

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
