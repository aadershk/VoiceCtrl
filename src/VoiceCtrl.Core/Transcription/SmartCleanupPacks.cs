using System.IO;
using System.IO.Compression;
using VoiceCtrl.Core.Config;

namespace VoiceCtrl.Core.Transcription;

/// <summary>What Settings says about a Smart cleanup model, from one place.</summary>
/// <param name="Bytes">The download: the model file plus the llama.cpp server.</param>
public sealed record SmartCleanupPackInfo(SmartCleanupTier Tier, long Bytes);

/// <summary>
/// The language models Smart cleanup can run, each downloaded with the <c>llama-server</c> that runs
/// it. Every file is pinned to an exact revision and checked against its SHA-256 (research 33 §7), so
/// a re-uploaded or tampered file is refused. The weights are Apache-2.0 (Qwen) and the server is MIT
/// (llama.cpp); both are in THIRD-PARTY-NOTICES.md.
/// </summary>
public static class SmartCleanupPacks
{
    public const string ModelFile = "model.gguf";
    public const string ServerArchive = "llama-server.zip";

    private const string ServerUrl =
        "https://github.com/ggml-org/llama.cpp/releases/download/b11429/llama-b11429-bin-win-cpu-x64.zip";

    private const string ServerSha256 = "1283323272b04cd07905816a597a0da810918102de958f4ff6f7bbaa70ed2efe";
    private const long ServerBytes = 19_398_918;

    /// <summary>Qwen3-1.7B Q4_K_M, unsloth's quantisation of the official weights.</summary>
    public static OfflineModel Standard { get; } = new(
        "llm-qwen3-1.7b",
        "unsloth/Qwen3-1.7B-GGUF",
        OfflineModelKind.LanguageModel,
        [
            new("Qwen3-1.7B-Q4_K_M.gguf", ModelFile,
                "https://huggingface.co/unsloth/Qwen3-1.7B-GGUF/resolve/d7f544eead698dbd1f15126ef60b45a1e1933222/Qwen3-1.7B-Q4_K_M.gguf",
                "b139949c5bd74937ad8ed8c8cf3d9ffb1e99c866c823204dc42c0d91fa181897"),
            new("llama-server.zip", ServerArchive, ServerUrl, ServerSha256),
        ],
        1_107_409_472 + ServerBytes);

    /// <summary>Qwen3-4B-Instruct-2507 Q4_K_M, unsloth's quantisation of the official weights.</summary>
    public static OfflineModel Better { get; } = new(
        "llm-qwen3-4b",
        "unsloth/Qwen3-4B-Instruct-2507-GGUF",
        OfflineModelKind.LanguageModel,
        [
            new("Qwen3-4B-Instruct-2507-Q4_K_M.gguf", ModelFile,
                "https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF/resolve/a06e946bb6b655725eafa393f4a9745d460374c9/Qwen3-4B-Instruct-2507-Q4_K_M.gguf",
                "3605803b982cb64aead44f6c1b2ae36e3acdb41d8e46c8a94c6533bc4c67e597"),
            new("llama-server.zip", ServerArchive, ServerUrl, ServerSha256),
        ],
        2_497_281_120 + ServerBytes);

    public static IReadOnlyList<SmartCleanupPackInfo> All { get; } =
    [
        new(SmartCleanupTier.Standard, Standard.ApproxBytes),
        new(SmartCleanupTier.Better, Better.ApproxBytes),
    ];

    public static OfflineModel For(SmartCleanupTier tier) => tier == SmartCleanupTier.Standard ? Standard : Better;

    /// <summary>The server inside a finished pack folder, unpacking it on first use. Null when the
    /// pack is not complete or its archive does not hold the server.</summary>
    public static string? ServerExe(string packFolder)
    {
        string exe = Path.Combine(packFolder, "server", "llama-server.exe");
        if (File.Exists(exe))
        {
            return exe;
        }

        string archive = Path.Combine(packFolder, ServerArchive);
        if (!File.Exists(archive))
        {
            return null;
        }

        string target = Path.Combine(packFolder, "server");
        try
        {
            ZipFile.ExtractToDirectory(archive, target, overwriteFiles: true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }

        return File.Exists(exe) ? exe : null;
    }

    public static string ModelPath(string packFolder) => Path.Combine(packFolder, ModelFile);
}
