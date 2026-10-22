using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace VoiceCtrl.Core.Updates;

/// <summary>What a release's signed manifest vouches for: one package, by name and SHA-256.</summary>
/// <param name="Sha256">Lower-case hex.</param>
public sealed record ReleaseManifest(string PackageId, string Version, string PackageFile, string Sha256);

/// <summary>
/// The ed25519 layer on top of Velopack's own hashes (spec §9, research 01 §3). Velopack's feed
/// carries each package's hash, but whoever can edit the GitHub release can change both together;
/// this signature needs the private key, which lives only in a GitHub secret. CI signs each
/// release's manifest (<c>tools/SignRelease</c>); the app checks it against the public key compiled
/// in here before it keeps, or later installs, a downloaded package.
///
/// The manifest file is a small envelope: the manifest JSON as base64, and the signature over
/// exactly those bytes, so nothing depends on how JSON is re-serialised.
/// </summary>
public static class ReleaseSigning
{
    /// <summary>Velopack's pack ID (release.yml), which also names the manifest file.</summary>
    public const string PackageId = "VoiceCtrlApp";

    // Made for v4.0.0 with `SignRelease keygen` (2026-10-22). The private half is only in the
    // VOICECTRL_ED25519_PRIVATE_KEY secret (and the owner's backup). Changing this key strands
    // every installed copy unless the release that changes it is signed with the old key: see
    // `SignRelease sign --handover`, which moved the 4.0.0-rc builds over to this key.
    private const string PublicKeyBase64 = "/GG/9TOMDJcnIvIhjmB1iHFhJWt7FbmrDMTK57MWxuE=";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The compiled-in key every update must be signed with.</summary>
    public static byte[] PublicKey => Convert.FromBase64String(PublicKeyBase64);

    /// <summary>Sits next to the package in the feed, e.g. <c>VoiceCtrlApp-4.1.0-manifest.json</c>.</summary>
    public static string ManifestFileName(string version) => $"{PackageId}-{version}-manifest.json";

    /// <summary>A new keypair as base64 (the private key is the 32-byte seed).</summary>
    public static (string PrivateKey, string PublicKey) CreateKeyPair()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        return (
            Convert.ToBase64String(((Ed25519PrivateKeyParameters)pair.Private).GetEncoded()),
            Convert.ToBase64String(((Ed25519PublicKeyParameters)pair.Public).GetEncoded()));
    }

    /// <summary>The manifest file's bytes, signed with <paramref name="privateKeyBase64"/>.</summary>
    public static byte[] Sign(ReleaseManifest manifest, string privateKeyBase64)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(Convert.FromBase64String(privateKeyBase64)));
        signer.BlockUpdate(payload, 0, payload.Length);
        byte[] signature = signer.GenerateSignature();

        var envelope = new Envelope(Convert.ToBase64String(payload), Convert.ToBase64String(signature));
        return JsonSerializer.SerializeToUtf8Bytes(envelope, Json);
    }

    /// <summary>
    /// Checks the signature and that the manifest is this app's, for <paramref name="expectedVersion"/>
    /// (so an old genuine manifest can't vouch for a newer package). Throws
    /// <see cref="UpdateVerificationException"/> on anything else.
    /// </summary>
    public static ReleaseManifest Verify(byte[] manifestFile, byte[] publicKey, string expectedVersion)
    {
        byte[] payload;
        byte[] signature;
        try
        {
            Envelope? envelope = JsonSerializer.Deserialize<Envelope>(manifestFile, Json);
            payload = Convert.FromBase64String(envelope?.Payload ?? throw Refuse("the manifest is empty"));
            signature = Convert.FromBase64String(envelope.Signature ?? throw Refuse("the manifest has no signature"));
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw Refuse($"the manifest can't be read ({ex.GetType().Name})");
        }

        bool signed;
        try
        {
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(publicKey));
            verifier.BlockUpdate(payload, 0, payload.Length);
            signed = verifier.VerifySignature(signature);
        }
        catch (ArgumentException)
        {
            signed = false;
        }

        if (!signed)
        {
            throw Refuse("the manifest's signature doesn't match VoiceCtrl's key");
        }

        ReleaseManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ReleaseManifest>(payload, Json);
        }
        catch (JsonException)
        {
            throw Refuse("the signed manifest can't be read");
        }

        if (manifest is null || manifest.PackageId != PackageId)
        {
            throw Refuse("the manifest is for another app");
        }

        if (manifest.Version != expectedVersion)
        {
            throw Refuse($"the manifest is for {manifest.Version}, not {expectedVersion}");
        }

        return manifest;
    }

    /// <summary>Throws <see cref="UpdateVerificationException"/> unless the file at
    /// <paramref name="packagePath"/> is the one the manifest names, byte for byte.</summary>
    public static void VerifyPackage(ReleaseManifest manifest, string packagePath)
    {
        if (!string.Equals(Path.GetFileName(packagePath), manifest.PackageFile, StringComparison.OrdinalIgnoreCase))
        {
            throw Refuse($"the manifest names {manifest.PackageFile}, not {Path.GetFileName(packagePath)}");
        }

        string actual;
        try
        {
            actual = Sha256Of(packagePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Refuse($"the package can't be read ({ex.GetType().Name})");
        }

        if (!string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Refuse("the package's SHA-256 doesn't match the signed manifest");
        }
    }

    /// <summary>Lower-case hex SHA-256 of a file.</summary>
    public static string Sha256Of(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static UpdateVerificationException Refuse(string why) => new($"Update refused: {why}.");

    private sealed record Envelope(string? Payload, string? Signature);
}
