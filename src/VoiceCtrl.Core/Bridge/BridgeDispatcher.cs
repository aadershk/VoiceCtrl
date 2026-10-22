using System.Buffers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using VoiceCtrl.Core.Logging;

namespace VoiceCtrl.Core.Bridge;

/// <summary>
/// A command handler's way to refuse: the page gets <c>ok:false</c> with this stable code. The
/// message is for logs and developers, never shown to the user as-is.
/// </summary>
public sealed class BridgeCommandException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// The host side of one surface's bridge (spec §4): reads the page's command envelopes, checks
/// where they came from, runs the registered handler with typed arguments and writes the reply
/// envelope. One instance per surface, so a page can only reach the commands registered for it.
///
/// Threading: call <see cref="DispatchAsync"/> on the UI thread; handlers continue there.
/// </summary>
public sealed class BridgeDispatcher(Uri appOrigin)
{
    private readonly Dictionary<string, Func<JsonElement?, Task<Reply>>> _handlers = new(StringComparer.Ordinal);

    /// <summary>Registers the handler for the command whose arguments are <typeparamref name="TArgs"/>.
    /// Throws when the contract declares a different result type, or the command already has one.</summary>
    public void Handle<TArgs, TResult>(Func<TArgs, Task<TResult>> handler)
        where TArgs : class
    {
        JsonTypeInfo<TResult> resultInfo = TypeInfo<TResult>();
        Register<TArgs>(typeof(TResult), async args => Reply.Success(Serialize(await handler(args), resultInfo)));
    }

    /// <summary>Registers the handler for a command that answers null.</summary>
    public void Handle<TArgs>(Func<TArgs, Task> handler)
        where TArgs : class =>
        Register<TArgs>(null, async args =>
        {
            await handler(args);
            return Reply.Success(null);
        });

    /// <summary>
    /// Handles one message from the page. Returns the reply envelope to post back, or null when the
    /// message must be ignored: it didn't come from the app origin, or it isn't a well-formed command
    /// (no id to answer). Never throws.
    /// </summary>
    /// <param name="source">WebView2's <c>WebMessageReceivedEventArgs.Source</c>: the page's URL.</param>
    public async Task<string?> DispatchAsync(string source, string messageJson)
    {
        if (!IsFromAppOrigin(source) || !TryReadCommand(messageJson, out long id, out string name, out JsonElement? args))
        {
            return null;
        }

        if (!_handlers.TryGetValue(name, out Func<JsonElement?, Task<Reply>>? handler))
        {
            return ResultMessage(id, Reply.Failure("unknown_command", $"No command '{name}' on this surface."));
        }

        Reply reply;
        try
        {
            reply = await handler(args);
        }
        catch (BridgeCommandException ex)
        {
            reply = Reply.Failure(ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            // The exception text can carry paths or other details; the log keeps them, the page
            // (a system boundary) gets a stable code only.
            SimpleFileLogger.LogError($"Bridge command {name}", ex);
            reply = Reply.Failure("failed", "The command failed; see the log.");
        }

        return ResultMessage(id, reply);
    }

    /// <summary>The host → page envelope for an event, ready for <c>PostWebMessageAsJson</c>.</summary>
    public static string EventMessage<TEvent>(TEvent payload)
        where TEvent : class
    {
        BridgeEventAttribute contract = typeof(TEvent).GetCustomAttribute<BridgeEventAttribute>()
            ?? throw new InvalidOperationException($"{typeof(TEvent).Name} has no [BridgeEvent].");

        return Write(writer =>
        {
            writer.WriteString("t", "evt");
            writer.WriteString("name", contract.Name);
            writer.WritePropertyName("payload");
            JsonSerializer.Serialize(writer, payload, TypeInfo<TEvent>());
        });
    }

    private void Register<TArgs>(Type? declaredResult, Func<TArgs, Task<Reply>> run)
        where TArgs : class
    {
        BridgeCommandAttribute contract = typeof(TArgs).GetCustomAttribute<BridgeCommandAttribute>()
            ?? throw new InvalidOperationException($"{typeof(TArgs).Name} has no [BridgeCommand].");

        if (contract.Result != declaredResult)
        {
            throw new InvalidOperationException(
                $"{contract.Name} answers {contract.Result?.Name ?? "null"}, not {declaredResult?.Name ?? "null"}.");
        }

        if (_handlers.ContainsKey(contract.Name))
        {
            throw new InvalidOperationException($"{contract.Name} already has a handler on this surface.");
        }

        JsonTypeInfo<TArgs> argsInfo = TypeInfo<TArgs>();
        _handlers[contract.Name] = element =>
        {
            TArgs? args;
            try
            {
                args = element is { } json ? json.Deserialize(argsInfo) : JsonSerializer.Deserialize("{}", argsInfo);
            }
            catch (JsonException ex)
            {
                return Task.FromResult(Reply.Failure("bad_args", ex.Message));
            }

            return args is null
                ? Task.FromResult(Reply.Failure("bad_args", "Arguments must be an object."))
                : run(args);
        };
    }

    private bool IsFromAppOrigin(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out Uri? uri)
        && string.Equals(uri.Scheme, appOrigin.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Host, appOrigin.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == appOrigin.Port;

    private static bool TryReadCommand(string messageJson, out long id, out string name, out JsonElement? args)
    {
        id = 0;
        name = "";
        args = null;

        JsonElement root;
        try
        {
            using JsonDocument document = JsonDocument.Parse(messageJson);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return false;
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("t", out JsonElement t) || t.ValueKind != JsonValueKind.String || t.GetString() != "cmd"
            || !root.TryGetProperty("id", out JsonElement idElement) || idElement.ValueKind != JsonValueKind.Number
            || !idElement.TryGetInt64(out id)
            || !root.TryGetProperty("name", out JsonElement nameElement) || nameElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        name = nameElement.GetString()!;
        if (root.TryGetProperty("args", out JsonElement argsElement) && argsElement.ValueKind != JsonValueKind.Null)
        {
            args = argsElement;
        }

        return true;
    }

    private static string ResultMessage(long id, Reply reply) => Write(writer =>
    {
        writer.WriteString("t", "res");
        writer.WriteNumber("id", id);
        writer.WriteBoolean("ok", reply.Ok);
        if (reply.Ok)
        {
            writer.WritePropertyName("value");
            if (reply.ValueJson is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteRawValue(reply.ValueJson, skipInputValidation: true);
            }
        }
        else
        {
            writer.WriteStartObject("error");
            writer.WriteString("code", reply.ErrorCode);
            writer.WriteString("message", reply.ErrorMessage);
            writer.WriteEndObject();
        }
    });

    private static string Write(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string Serialize<T>(T value, JsonTypeInfo<T> info) => JsonSerializer.Serialize(value, info);

    private static JsonTypeInfo<T> TypeInfo<T>() =>
        BridgeJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
        ?? throw new InvalidOperationException($"{typeof(T).Name} is not in BridgeJsonContext.");

    private readonly record struct Reply(bool Ok, string? ValueJson, string? ErrorCode, string? ErrorMessage)
    {
        public static Reply Success(string? valueJson) => new(true, valueJson, null, null);

        public static Reply Failure(string code, string message) => new(false, null, code, message);
    }
}
