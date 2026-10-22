using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using VoiceCtrl.Core.Interop;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Transcription;

/// <summary>Where one answer's time went, as the server counts it.</summary>
/// <param name="CachedTokens">Prompt tokens the server already held from an earlier request.</param>
/// <param name="PromptTokens">Prompt tokens it had to read for this one.</param>
internal sealed record ModelTimings(int CachedTokens, int PromptTokens, double PromptMs, int WrittenTokens, double WrittenMs);

/// <summary>
/// The local language model, run as <c>llama-server</c> in a child process (research 33 §3). A crash
/// or hang in the model cannot take dictation down, since the engine then pastes the rule-cleaned
/// text; killing the process is the unload. It listens only on loopback, on a port the system picks,
/// and a job object ends it with VoiceCtrl.
/// </summary>
internal sealed class LlamaServerModel : ISmartCleanupModel
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

    /// <summary>A prime is saved only when its slot had next to nothing cached, so the rules were read
    /// from scratch. Switching to another kind of app re-reads only that app's notes (about 370 tokens)
    /// and keeps the saved rules, which still restore as the shared start of every prompt.</summary>
    private const int ColdCacheTokens = 64;
    private const int SlotCount = 2;

    private readonly string _serverExe;
    private readonly string _modelPath;
    private readonly int _threads;
    private readonly IReadOnlyList<string> _extraArguments;
    private readonly string? _slotFolder;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _http = new();

    private Process? _process;
    private ChildProcessJob? _job;
    private Uri? _baseAddress;

    /// <param name="extraArguments">Appended to the server's command line: the speed settings the
    /// bench found best, or a GPU layer count.</param>
    /// <param name="slotFolder">Where primed rules are kept between runs of the server; null keeps
    /// nothing (the benches).</param>
    public LlamaServerModel(
        string serverExe, string modelPath, int threads, IReadOnlyList<string>? extraArguments = null, string? slotFolder = null)
    {
        _serverExe = serverExe;
        _modelPath = modelPath;
        _threads = threads;
        _extraArguments = extraArguments ?? [];
        _slotFolder = slotFolder;
    }

    /// <summary>Starts the server if it is not running and waits until it has loaded the model.
    /// Called when the Overlay appears, so the load overlaps with the user speaking.</summary>
    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureRunningAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        Uri baseAddress;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureRunningAsync(cancellationToken).ConfigureAwait(false);
            baseAddress = _baseAddress!;
        }
        finally
        {
            _gate.Release();
        }

        JsonArray messages = [new JsonObject { ["role"] = "system", ["content"] = request.System }];
        foreach ((string user, string assistant) in request.Examples)
        {
            messages.Add(new JsonObject { ["role"] = "user", ["content"] = user });
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = assistant });
        }

        messages.Add(new JsonObject { ["role"] = "user", ["content"] = request.User });

        JsonObject body = new()
        {
            ["messages"] = messages,
            ["temperature"] = 0,
            ["max_tokens"] = request.MaxTokens,

            // The static prompt (rules, hints, examples) is the same for every Dictation into one app,
            // so the server keeps its processed form and only reads the transcript afresh.
            ["cache_prompt"] = true,

            // Qwen3 answers inside <think> unless told not to, which costs seconds for nothing here.
            ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
        };

        if (request.KeepInSlot is { } slot)
        {
            body["id_slot"] = slot;
        }

        using HttpResponseMessage response = await _http
            .PostAsJsonAsync(new Uri(baseAddress, "v1/chat/completions"), body, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        JsonNode? json = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken).ConfigureAwait(false);
        ModelTimings? measured = ReportTimings(json?["timings"]);
        if (request.KeepInSlot is { } kept && measured is { CachedTokens: < ColdCacheTokens, PromptTokens: >= ColdCacheTokens })
        {
            await SlotActionAsync(baseAddress, kept, "save", cancellationToken).ConfigureAwait(false);
        }

        return json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
    }

    private static string SlotFile(int slot) => $"slot{slot}.bin";

    /// <summary>
    /// Saves or restores one slot's processed prompt (llama-server's slot files): 0.4 s to save and
    /// 0.2 s to restore what took 36 s to read (Better model, Ryzen 7 4800HS, 2026-10-21). Best-effort:
    /// a file the server can't use (another server version, a damaged write) is deleted, and the next
    /// prime reads the rules afresh and saves them again.
    /// </summary>
    private async Task<bool> SlotActionAsync(Uri baseAddress, int slot, string action, CancellationToken cancellationToken)
    {
        if (_slotFolder is null)
        {
            return false;
        }

        string file = Path.Combine(_slotFolder, SlotFile(slot));
        try
        {
            if (action == "restore" && !File.Exists(file))
            {
                return false;
            }

            using HttpResponseMessage response = await _http
                .PostAsJsonAsync(new Uri(baseAddress, $"slots/{slot}?action={action}"), new JsonObject { ["filename"] = SlotFile(slot) }, cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                SimpleFileLogger.LogInfo($"Smart cleanup: {(action == "save" ? "saved" : "restored")} the rules in slot {slot}.");
                return true;
            }

            SimpleFileLogger.LogInfo($"Smart cleanup: could not {action} slot {slot} ({(int)response.StatusCode}); it is read afresh.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            SimpleFileLogger.LogInfo($"Smart cleanup: could not {action} slot {slot} ({ex.GetType().Name}); it is read afresh.");
        }

        if (action == "restore")
        {
            TryDelete(file);
        }

        return false;
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Overwritten by the next save instead.
        }
    }

    /// <summary>Raised with the server's own count of each answer's work: tokens read (and how many
    /// of those came from its cache) and tokens written, with the time each took. For the benches.</summary>
    internal static event Action<ModelTimings>? Timed;

    /// <summary>Logs where an answer's time went: reading the prompt or writing the answer. Counts
    /// and times only, never text. Returns the counts, or null when the server sent none.</summary>
    private static ModelTimings? ReportTimings(JsonNode? timings)
    {
        if (timings is null)
        {
            return null;
        }

        try
        {
            ModelTimings measured = new(
                timings["cache_n"]?.GetValue<int>() ?? 0,
                timings["prompt_n"]?.GetValue<int>() ?? 0,
                timings["prompt_ms"]?.GetValue<double>() ?? 0,
                timings["predicted_n"]?.GetValue<int>() ?? 0,
                timings["predicted_ms"]?.GetValue<double>() ?? 0);
            SimpleFileLogger.LogInfo(
                $"Language model: read {measured.PromptTokens} tokens in {measured.PromptMs:0} ms ({measured.CachedTokens} cached), " +
                $"wrote {measured.WrittenTokens} in {measured.WrittenMs:0} ms.");
            Timed?.Invoke(measured);
            return measured;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            // A server version that reports these differently costs only the log line.
            return null;
        }
    }

    /// <summary>Caller holds the gate.</summary>
    private async Task EnsureRunningAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        StopProcess();

        int port = FreeLoopbackPort();
        ProcessStartInfo start = new(_serverExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_serverExe)!,
        };

        // Loopback only, no web page, and a context just big enough for the rules plus a long
        // Dictation: a larger context only costs memory. Two slots of 4096 tokens, so the cleanup
        // rules and the word-mending rules each stay read in a slot of their own; with one slot every
        // call would evict the other's rules and read them again (12 s cold on the Better model).
        // Writing is memory-bound, so extra threads add nothing there; reading the prompt is compute-bound
        // and runs on every logical core with flash attention: 57 to 77 tokens/s on the Better model
        // (llama-bench, Ryzen 7 4800HS, ticket 40).
        foreach (string argument in new[]
                 {
                     "--model", _modelPath, "--host", "127.0.0.1", "--port", port.ToString(), "--threads", _threads.ToString(),
                     "--threads-batch", Math.Max(_threads, Environment.ProcessorCount).ToString(), "--flash-attn", "on",
                     "--ctx-size", "8192", "--parallel", "2", "--no-webui", "--no-warmup",
                 })
        {
            start.ArgumentList.Add(argument);
        }

        foreach (string argument in _extraArguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (_slotFolder is not null)
        {
            Directory.CreateDirectory(_slotFolder);
            start.ArgumentList.Add("--slot-save-path");
            start.ArgumentList.Add(_slotFolder);
        }

        SimpleFileLogger.LogInfo("Starting the Smart cleanup model...");
        _job ??= new ChildProcessJob();
        _process = Process.Start(start) ?? throw new InvalidOperationException("llama-server did not start.");
        _job.Add(_process);
        _baseAddress = new Uri($"http://127.0.0.1:{port}/");

        await WaitUntilReadyAsync(cancellationToken).ConfigureAwait(false);
        SimpleFileLogger.LogInfo("Smart cleanup model ready.");

        // The rules a previous run primed, back in their slots before the next prime asks for them.
        for (int slot = 0; slot < SlotCount; slot++)
        {
            await SlotActionAsync(_baseAddress, slot, "restore", cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartTimeout);

        try
        {
            while (true)
            {
                if (_process!.HasExited)
                {
                    throw new InvalidOperationException($"llama-server stopped with code {_process.ExitCode}.");
                }

                try
                {
                    using HttpResponseMessage health = await _http.GetAsync(new Uri(_baseAddress!, "health"), timeout.Token).ConfigureAwait(false);
                    if (health.StatusCode == HttpStatusCode.OK)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // Not listening yet.
                }

                await Task.Delay(100, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            StopProcess();
            throw new TimeoutException("llama-server did not become ready in time.");
        }
        catch
        {
            StopProcess();
            throw;
        }
    }

    private static int FreeLoopbackPort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>Unloads the model by ending the process. The next call starts it again.</summary>
    public void Unload()
    {
        _gate.Wait();
        try
        {
            StopProcess();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void StopProcess()
    {
        Process? process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Dispose()
    {
        StopProcess();
        _job?.Dispose();
        _http.Dispose();
        _gate.Dispose();
    }
}
