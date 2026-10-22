using VoiceCtrl.Core.Config;

namespace VoiceCtrl.Core.Transcription;

/// <summary>How sherpa-onnx has to be configured to run a model.</summary>
public enum OfflineModelKind
{
    /// <summary>encoder, decoder, joiner and tokens: the Parakeet-TDT (NeMo transducer) models.</summary>
    NemoTransducer,

    /// <summary>encoder, decoder and tokens: a Whisper export.</summary>
    Whisper,

    /// <summary>A language model (a GGUF file) with the <c>llama-server</c> that runs it: Smart cleanup.</summary>
    LanguageModel,
}

/// <param name="Remote">The file's name in the Hugging Face repo.</param>
/// <param name="Local">The name it is saved under, the same for every model of one kind, so the
/// recognizer setup never depends on how a repo named its files.</param>
/// <param name="Url">Where to fetch it when that is not the model's Hugging Face repo.</param>
/// <param name="Sha256">The file's hash, lower-case hex. A download that does not match is deleted
/// and fails, so a changed or tampered file is never run.</param>
public sealed record OfflineModelFile(string Remote, string Local, string? Url = null, string? Sha256 = null);

/// <summary>One downloadable offline speech model: where it comes from, what is in it, how big it is.</summary>
/// <param name="Directory">The folder under <c>models\</c> it is kept in.</param>
/// <param name="ApproxBytes">The sum of the files, for progress. Never trusted for correctness.</param>
public sealed record OfflineModel(
    string Directory,
    string Repo,
    OfflineModelKind Kind,
    IReadOnlyList<OfflineModelFile> Files,
    long ApproxBytes)
{
    /// <summary>The model's folder under the models root.</summary>
    public string PathUnder(string modelsRoot) => System.IO.Path.Combine(modelsRoot, Directory);
}

/// <summary>
/// The models VoiceCtrl can run Offline. Sizes are the real int8 files, measured on 2026-09-30
/// (ticket 22): European 670 MB, All languages 1.04 GB. See <see cref="OfflinePackCatalog"/> for what
/// the Settings row says about them.
/// </summary>
public static class OfflineModels
{
    private static readonly OfflineModelFile[] TransducerFiles =
    [
        new("encoder.int8.onnx", "encoder.int8.onnx"),
        new("decoder.int8.onnx", "decoder.int8.onnx"),
        new("joiner.int8.onnx", "joiner.int8.onnx"),
        new("tokens.txt", "tokens.txt"),
    ];

    /// <summary>The English model from Setup. Its size is the long-standing "about 700 MB" figure.</summary>
    public static OfflineModel English { get; } = new(
        ConfigLoader.DefaultLocalModelVariant,
        "csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8",
        OfflineModelKind.NemoTransducer,
        TransducerFiles,
        700L * 1024 * 1024);

    public static OfflineModel European { get; } = new(
        "parakeet-tdt-0.6b-v3",
        "csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8",
        OfflineModelKind.NemoTransducer,
        TransducerFiles,
        670_478_772);

    /// <summary>Whisper turbo. Large-v3 would be 1.78 GB and several times slower for little gain.</summary>
    public static OfflineModel AllLanguages { get; } = new(
        "whisper-turbo",
        "csukuangfj/sherpa-onnx-whisper-turbo",
        OfflineModelKind.Whisper,
        [
            new("turbo-encoder.int8.onnx", "encoder.int8.onnx"),
            new("turbo-decoder.int8.onnx", "decoder.int8.onnx"),
            new("turbo-tokens.txt", "tokens.txt"),
        ],
        1_036_613_791);

    /// <summary>The model for a pack; <see cref="OfflinePack.None"/> is English.</summary>
    public static OfflineModel For(OfflinePack pack) => pack switch
    {
        OfflinePack.European => European,
        OfflinePack.AllLanguages => AllLanguages,
        _ => English,
    };
}

/// <summary>What Settings says about a pack, all from one place (measured 2026-09-30, ticket 22).</summary>
/// <param name="Languages">"About" for the Whisper pack: it lists 99.</param>
/// <param name="Slower">Slower than English: the Whisper pack decodes a 10 s clip in about 7 s on a
/// 4-thread CPU where the others take under 1 s.</param>
public sealed record OfflinePackInfo(OfflinePack Pack, long Bytes, int Languages, bool Slower);

public static class OfflinePackCatalog
{
    public static IReadOnlyList<OfflinePackInfo> All { get; } =
    [
        new(OfflinePack.European, OfflineModels.European.ApproxBytes, 25, Slower: false),
        new(OfflinePack.AllLanguages, OfflineModels.AllLanguages.ApproxBytes, 99, Slower: true),
    ];
}
