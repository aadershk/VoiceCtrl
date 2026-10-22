using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using VoiceCtrl.Core.Transcription;

namespace VoiceCtrl.Core.Tests.Transcription;

public sealed class LocalModelDownloaderTests : IDisposable
{
    private static readonly string[] Files = ["encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt"];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"voicectrl-test-model-{Guid.NewGuid()}");

    [Fact]
    public async Task DownloadAll_WritesEveryFile_AndReportsBytesAcrossFiles()
    {
        var server = new FakeModelServer();
        var reports = new List<long>();

        await LocalModelDownloader.DownloadAllAsync(_dir, server.Client, new SyncProgress(reports.Add), CancellationToken.None);

        foreach (string name in Files)
        {
            Assert.Equal(server.Content(name), File.ReadAllBytes(Path.Combine(_dir, name)));
        }

        Assert.Equal(server.TotalBytes, reports[^1]);
        Assert.True(LocalModelDownloader.IsFullyDownloaded(_dir));
    }

    [Fact]
    public async Task AStoppedDownload_ResumesFromThePartialFile_WithARangeRequest()
    {
        var server = new FakeModelServer { FailAfterBytes = 3_000 };

        await Assert.ThrowsAnyAsync<IOException>(() =>
            LocalModelDownloader.DownloadAllAsync(_dir, server.Client, null, CancellationToken.None));
        long onDisk = LocalModelDownloader.BytesOnDisk(_dir);
        Assert.Equal(3_000, onDisk);

        server.FailAfterBytes = null;
        var reports = new List<long>();
        await LocalModelDownloader.DownloadAllAsync(_dir, server.Client, new SyncProgress(reports.Add), CancellationToken.None);

        Assert.Equal($"bytes={3_000}-", server.LastRange("encoder.int8.onnx"));
        Assert.Equal(server.Content("encoder.int8.onnx"), File.ReadAllBytes(Path.Combine(_dir, "encoder.int8.onnx")));
        Assert.True(reports[0] > onDisk, "progress continues from what was already on disk");
        Assert.Equal(server.TotalBytes, reports[^1]);
    }

    [Fact]
    public async Task AServerThatIgnoresTheRange_StartsTheFileOver()
    {
        var server = new FakeModelServer { FailAfterBytes = 3_000 };
        await Assert.ThrowsAnyAsync<IOException>(() =>
            LocalModelDownloader.DownloadAllAsync(_dir, server.Client, null, CancellationToken.None));

        server.FailAfterBytes = null;
        server.IgnoreRange = true;
        await LocalModelDownloader.DownloadAllAsync(_dir, server.Client, null, CancellationToken.None);

        Assert.Equal(server.Content("encoder.int8.onnx"), File.ReadAllBytes(Path.Combine(_dir, "encoder.int8.onnx")));
    }

    [Fact]
    public async Task APartialFileThatIsAlreadyComplete_IsKept()
    {
        Directory.CreateDirectory(_dir);
        var server = new FakeModelServer();
        File.WriteAllBytes(Path.Combine(_dir, "encoder.int8.onnx.download"), server.Content("encoder.int8.onnx"));

        await LocalModelDownloader.DownloadAllAsync(_dir, server.Client, null, CancellationToken.None);

        Assert.Equal(server.Content("encoder.int8.onnx"), File.ReadAllBytes(Path.Combine(_dir, "encoder.int8.onnx")));
    }

    [Fact]
    public async Task AStalledConnection_FailsInsteadOfHanging()
    {
        var server = new FakeModelServer { StallAfterBytes = 1_000 };

        await Assert.ThrowsAsync<TimeoutException>(() =>
            LocalModelDownloader.DownloadAllAsync(_dir, server.Client, null, CancellationToken.None, stallTimeout: TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public async Task TwoDownloadsAtOnce_TakeTurns_AndBothFinish()
    {
        var server = new FakeModelServer();

        await Task.WhenAll(
            LocalModelDownloader.DownloadAllAsync(_dir, server.Client, null, CancellationToken.None),
            LocalModelDownloader.DownloadAllAsync(_dir, server.Client, null, CancellationToken.None));

        Assert.True(LocalModelDownloader.IsFullyDownloaded(_dir));
        Assert.Equal(Files.Length, server.Requests);
    }

    [Fact]
    public async Task APackModel_IsFetchedFromItsOwnRepo_AndSavedUnderTheSharedFileNames()
    {
        var server = new FakeModelServer();
        var requested = new List<string>();
        server.OnRequest = uri => requested.Add(uri.ToString());

        await LocalModelDownloader.DownloadAllAsync(_dir, server.Client, null, CancellationToken.None, model: OfflineModels.AllLanguages);

        Assert.All(requested, url => Assert.StartsWith("https://huggingface.co/csukuangfj/sherpa-onnx-whisper-turbo/resolve/main/turbo-", url));
        Assert.Equal(
            ["decoder.int8.onnx", "encoder.int8.onnx", "tokens.txt"],
            Directory.GetFiles(_dir).Select(path => Path.GetFileName(path)!).Order().ToArray());
        Assert.Equal(server.Content("turbo-encoder.int8.onnx"), File.ReadAllBytes(Path.Combine(_dir, "encoder.int8.onnx")));
    }

    [Fact]
    public async Task AFileWithAPinnedUrlAndHash_IsFetchedFromThatUrl_AndKept()
    {
        var server = new FakeModelServer();
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(server.Content("model.gguf")));
        OfflineModel pinned = PinnedModel(hash);
        var requested = new List<string>();
        server.OnRequest = uri => requested.Add(uri.ToString());

        await LocalModelDownloader.DownloadAllAsync(_dir, server.Client, null, CancellationToken.None, model: pinned);

        Assert.Equal(["https://example.test/pinned/rev1/model.gguf"], requested);
        Assert.Equal(server.Content("model.gguf"), File.ReadAllBytes(Path.Combine(_dir, "model.gguf")));
    }

    [Fact]
    public async Task AFileThatDoesNotMatchItsHash_IsDeletedAndFails()
    {
        var server = new FakeModelServer();
        OfflineModel pinned = PinnedModel(new string('0', 64));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            LocalModelDownloader.DownloadAllAsync(_dir, server.Client, null, CancellationToken.None, model: pinned));

        Assert.False(File.Exists(Path.Combine(_dir, "model.gguf")));
        Assert.False(LocalModelDownloader.IsFullyDownloaded(_dir, pinned));
    }

    private static OfflineModel PinnedModel(string sha256) => new(
        "llm-test",
        "ignored/repo",
        OfflineModelKind.LanguageModel,
        [new("model.gguf", "model.gguf", "https://example.test/pinned/rev1/model.gguf", sha256)],
        5_000);

    [Fact]
    public async Task IsFullyDownloaded_ChecksTheFilesTheModelNeeds()
    {
        Directory.CreateDirectory(_dir);
        foreach (string name in new[] { "encoder.int8.onnx", "decoder.int8.onnx", "tokens.txt" })
        {
            await File.WriteAllBytesAsync(Path.Combine(_dir, name), [1]);
        }

        Assert.True(LocalModelDownloader.IsFullyDownloaded(_dir, OfflineModels.AllLanguages), "Whisper has no joiner");
        Assert.False(LocalModelDownloader.IsFullyDownloaded(_dir), "the English model needs its joiner");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    /// <summary>Reports on the calling thread, unlike <see cref="Progress{T}"/>.</summary>
    private sealed class SyncProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    /// <summary>Serves each model file as a few KB of known bytes, honouring Range like Hugging Face.</summary>
    private sealed class FakeModelServer : HttpMessageHandler
    {
        private readonly Dictionary<string, string?> _ranges = new();

        public FakeModelServer() => Client = new HttpClient(this);

        public HttpClient Client { get; }

        public long? FailAfterBytes { get; set; }

        public long? StallAfterBytes { get; set; }

        public bool IgnoreRange { get; set; }

        public int Requests { get; private set; }

        public Action<Uri>? OnRequest { get; set; }

        public long TotalBytes => Files.Sum(name => (long)Content(name).Length);

        public byte[] Content(string name)
        {
            int length = name == "tokens.txt" ? 500 : 5_000 + name.Length;
            if (name == "model.gguf")
            {
                length = 5_000;
            }

            return Enumerable.Range(0, length).Select(i => (byte)((i * 31) + name.Length)).ToArray();
        }

        public string? LastRange(string name) => _ranges.GetValueOrDefault(name);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            OnRequest?.Invoke(request.RequestUri!);
            string name = request.RequestUri!.Segments[^1];
            RangeHeaderValue? range = request.Headers.Range;
            _ranges[name] = range?.ToString();

            byte[] content = Content(name);
            long from = !IgnoreRange && range?.Ranges.FirstOrDefault()?.From is { } start ? start : 0;
            if (from >= content.Length)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable));
            }

            byte[] body = content[(int)from..];
            var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(new FlakyStream(body, FailAfterBytes, StallAfterBytes)),
            };
            return Task.FromResult(response);
        }
    }

    /// <summary>A response body that can drop the connection or go silent part-way through.</summary>
    private sealed class FlakyStream(byte[] body, long? failAfter, long? stallAfter) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => body.Length;

        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (failAfter is { } fail && _position >= fail)
            {
                throw new IOException("The connection dropped.");
            }

            if (stallAfter is { } stall && _position >= stall)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            long limit = Math.Min(body.Length, Math.Min(failAfter ?? long.MaxValue, stallAfter ?? long.MaxValue));
            int count = (int)Math.Min(buffer.Length, Math.Min(1_000, limit - _position));
            if (count <= 0 && _position >= body.Length)
            {
                return 0;
            }

            body.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
