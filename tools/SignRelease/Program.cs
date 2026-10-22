using VoiceCtrl.Core.Updates;

// SignRelease keygen <private-key-file>
//     Makes a new ed25519 keypair, writes the private key to the file (never to the console, so it
//     stays out of logs) and prints the public key. Run once: the public key goes into
//     ReleaseSigning, the private key into the VOICECTRL_ED25519_PRIVATE_KEY secret. Never commit it.
//
// SignRelease sign <package.nupkg> <version> [--handover]
//     Writes VoiceCtrlApp-<version>-manifest.json next to the package, signed with the key in
//     VOICECTRL_ED25519_PRIVATE_KEY, and checks it against the key compiled into the app, so a
//     release signed with the wrong key fails here rather than on users' PCs.
//
//     --handover signs with the retired key in VOICECTRL_ED25519_HANDOVER_KEY instead, checked
//     against RetiredPublicKey below. It is for the one release that changes the app's key: copies
//     already installed still trust the retired key, so that release must be signed with it, and
//     every release after it with the new one. Used for 4.0.0, whose new key replaced the one the
//     4.0.0-rc builds trust (the old private key was exposed in an agent transcript on 2026-10-22).

const string KeyVariable = "VOICECTRL_ED25519_PRIVATE_KEY";
const string HandoverKeyVariable = "VOICECTRL_ED25519_HANDOVER_KEY";

// The key the 4.0.0-rc builds were compiled with. Never trusted by the app again.
const string RetiredPublicKey = "sXnpUzvdh/axGjL9o8eN3vS2ezL7FOWTjZzShpZZU1M=";

switch (args)
{
    case ["keygen", string privateKeyFile]:
    {
        if (File.Exists(privateKeyFile))
        {
            Console.Error.WriteLine($"{privateKeyFile} already exists; not overwriting a key.");
            return 1;
        }

        (string privateKey, string publicKey) = ReleaseSigning.CreateKeyPair();
        File.WriteAllText(privateKeyFile, privateKey);
        Console.WriteLine($"Private key written to {privateKeyFile}");
        Console.WriteLine($"Public key: {publicKey}");
        return 0;
    }

    case ["sign", string package, string version]:
        return Sign(package, version, handover: false);

    case ["sign", string package, string version, "--handover"]:
        return Sign(package, version, handover: true);

    default:
        Console.Error.WriteLine("Usage: SignRelease keygen <private-key-file> | SignRelease sign <package.nupkg> <version> [--handover]");
        return 2;
}

static int Sign(string package, string version, bool handover)
{
    string variable = handover ? HandoverKeyVariable : KeyVariable;
    byte[] checkKey = handover ? Convert.FromBase64String(RetiredPublicKey) : ReleaseSigning.PublicKey;
    string? privateKey = Environment.GetEnvironmentVariable(variable);
    if (string.IsNullOrWhiteSpace(privateKey))
    {
        Console.Error.WriteLine($"{variable} is not set.");
        return 1;
    }

    var manifest = new ReleaseManifest(ReleaseSigning.PackageId, version, Path.GetFileName(package), ReleaseSigning.Sha256Of(package));
    byte[] signed = ReleaseSigning.Sign(manifest, privateKey.Trim());
    string output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(package))!, ReleaseSigning.ManifestFileName(version));

    // The same check the installed app runs, against the key that app trusts.
    try
    {
        ReleaseSigning.VerifyPackage(ReleaseSigning.Verify(signed, checkKey, version), package);
    }
    catch (UpdateVerificationException ex)
    {
        Console.Error.WriteLine($"The signed manifest doesn't pass the {(handover ? "rc builds'" : "app's own")} check: {ex.Message}");
        return 1;
    }

    File.WriteAllBytes(output, signed);
    Console.WriteLine($"Signed {manifest.PackageFile} ({manifest.Sha256}){(handover ? " with the retired key" : "")} -> {output}");
    return 0;
}
