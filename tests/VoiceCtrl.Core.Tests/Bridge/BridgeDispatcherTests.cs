using System.Text.Json;
using VoiceCtrl.Core.Bridge;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Status;

namespace VoiceCtrl.Core.Tests.Bridge;

public class BridgeDispatcherTests
{
    private const string AppPage = "https://ui.voicectrl.example/hub.html";
    private static readonly Uri AppOrigin = new("https://ui.voicectrl.example/");

    private static readonly AppSnapshot Snapshot = new(
        Theme.Dark,
        ThemePreference.System,
        ReducedMotion: true,
        Version: "4.0.0",
        Status: new AppStatus(AppStatusKind.ApiKeyNotWorking, "Ctrl", Paused: true, OfflineFallback: true),
        UpdatedTo: null);

    private static BridgeDispatcher NewDispatcher()
    {
        var dispatcher = new BridgeDispatcher(AppOrigin);
        dispatcher.Handle<AppReady, AppSnapshot>(_ => Task.FromResult(Snapshot));
        return dispatcher;
    }

    private static JsonElement Parse(string? json)
    {
        Assert.NotNull(json);
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public async Task Ready_AnswersTheSnapshotInCamelCase()
    {
        string? reply = await NewDispatcher().DispatchAsync(AppPage, """{"t":"cmd","id":7,"name":"app.ready","args":{}}""");

        JsonElement root = Parse(reply);
        Assert.Equal("res", root.GetProperty("t").GetString());
        Assert.Equal(7, root.GetProperty("id").GetInt64());
        Assert.True(root.GetProperty("ok").GetBoolean());
        JsonElement value = root.GetProperty("value");
        Assert.Equal("dark", value.GetProperty("theme").GetString());
        Assert.Equal("system", value.GetProperty("themePreference").GetString());
        Assert.True(value.GetProperty("reducedMotion").GetBoolean());
        Assert.Equal("4.0.0", value.GetProperty("version").GetString());
        JsonElement status = value.GetProperty("status");
        Assert.Equal("apiKeyNotWorking", status.GetProperty("kind").GetString());
        Assert.Equal("Ctrl", status.GetProperty("hotkey").GetString());
        Assert.True(status.GetProperty("paused").GetBoolean());
        Assert.True(status.GetProperty("offlineFallback").GetBoolean());
    }

    [Fact]
    public async Task CommandWithoutResult_AnswersNull_AndPassesTypedArgs()
    {
        BridgeDispatcher dispatcher = NewDispatcher();
        ThemePreference? received = null;
        dispatcher.Handle<SetTheme>(args =>
        {
            received = args.Theme;
            return Task.CompletedTask;
        });

        string? reply = await dispatcher.DispatchAsync(AppPage, """{"t":"cmd","id":2,"name":"app.setTheme","args":{"theme":"light"}}""");

        JsonElement root = Parse(reply);
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("value").ValueKind);
        Assert.Equal(ThemePreference.Light, received);
    }

    [Fact]
    public async Task MissingArgs_CountAsEmpty()
    {
        string? reply = await NewDispatcher().DispatchAsync(AppPage, """{"t":"cmd","id":1,"name":"app.ready"}""");

        Assert.True(Parse(reply).GetProperty("ok").GetBoolean());
    }

    [Theory]
    [InlineData("https://evil.example/hub.html")]
    [InlineData("http://ui.voicectrl.example/hub.html")]
    [InlineData("https://ui.voicectrl.example:8443/hub.html")]
    [InlineData("file:///C:/hub.html")]
    [InlineData("about:blank")]
    [InlineData("not a uri")]
    public async Task MessageFromAnotherOrigin_IsIgnored(string source)
    {
        bool called = false;
        var dispatcher = new BridgeDispatcher(AppOrigin);
        dispatcher.Handle<AppReady, AppSnapshot>(_ =>
        {
            called = true;
            return Task.FromResult(Snapshot);
        });

        string? reply = await dispatcher.DispatchAsync(source, """{"t":"cmd","id":1,"name":"app.ready","args":{}}""");

        Assert.Null(reply);
        Assert.False(called);
    }

    [Fact]
    public async Task DevServerOrigin_IsAcceptedWhenConfigured()
    {
        var dispatcher = new BridgeDispatcher(new Uri("http://localhost:5173/"));
        dispatcher.Handle<AppReady, AppSnapshot>(_ => Task.FromResult(Snapshot));

        string? reply = await dispatcher.DispatchAsync("http://localhost:5173/hub.html", """{"t":"cmd","id":1,"name":"app.ready"}""");

        Assert.True(Parse(reply).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task UnregisteredCommand_AnswersUnknownCommand()
    {
        string? reply = await NewDispatcher().DispatchAsync(AppPage, """{"t":"cmd","id":3,"name":"window.close","args":{}}""");

        JsonElement root = Parse(reply);
        Assert.Equal(3, root.GetProperty("id").GetInt64());
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("unknown_command", root.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("""{"theme":"purple"}""")]
    [InlineData("""{"theme":2}""")]
    [InlineData("\"light\"")]
    public async Task BadArgs_AnswerBadArgs_WithoutCallingTheHandler(string args)
    {
        BridgeDispatcher dispatcher = NewDispatcher();
        bool called = false;
        dispatcher.Handle<SetTheme>(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        string? reply = await dispatcher.DispatchAsync(AppPage, $$"""{"t":"cmd","id":4,"name":"app.setTheme","args":{{args}}}""");

        JsonElement root = Parse(reply);
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("bad_args", root.GetProperty("error").GetProperty("code").GetString());
        Assert.False(called);
    }

    [Fact]
    public async Task HandlerRejection_AnswersItsCode()
    {
        BridgeDispatcher dispatcher = NewDispatcher();
        dispatcher.Handle<CloseWindow>(_ => throw new BridgeCommandException("not_open", "No window."));

        string? reply = await dispatcher.DispatchAsync(AppPage, """{"t":"cmd","id":5,"name":"window.close","args":{}}""");

        JsonElement error = Parse(reply).GetProperty("error");
        Assert.Equal("not_open", error.GetProperty("code").GetString());
        Assert.Equal("No window.", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task HandlerCrash_AnswersFailed_WithoutLeakingTheExceptionText()
    {
        BridgeDispatcher dispatcher = NewDispatcher();
        dispatcher.Handle<CloseWindow>(async _ =>
        {
            await Task.Yield();
            throw new InvalidOperationException(@"C:\Users\someone\secret path");
        });

        string? reply = await dispatcher.DispatchAsync(AppPage, """{"t":"cmd","id":6,"name":"window.close","args":{}}""");

        JsonElement error = Parse(reply).GetProperty("error");
        Assert.Equal("failed", error.GetProperty("code").GetString());
        Assert.DoesNotContain("someone", error.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("42")]
    [InlineData("[]")]
    [InlineData("""{"t":"evt","name":"app.theme","payload":{}}""")]
    [InlineData("""{"t":"cmd","name":"app.ready"}""")]
    [InlineData("""{"t":"cmd","id":"7","name":"app.ready"}""")]
    [InlineData("""{"t":"cmd","id":7}""")]
    public async Task MalformedEnvelope_IsIgnored(string message)
    {
        Assert.Null(await NewDispatcher().DispatchAsync(AppPage, message));
    }

    [Fact]
    public void Handle_RejectsAResultTypeTheContractDoesNotDeclare()
    {
        var dispatcher = new BridgeDispatcher(AppOrigin);

        Assert.Throws<InvalidOperationException>(() => dispatcher.Handle<AppReady>(_ => Task.CompletedTask));
        Assert.Throws<InvalidOperationException>(() =>
            dispatcher.Handle<SetTheme, AppSnapshot>(_ => Task.FromResult(Snapshot)));
    }

    [Fact]
    public void Handle_RejectsASecondHandlerForTheSameCommand()
    {
        BridgeDispatcher dispatcher = NewDispatcher();

        Assert.Throws<InvalidOperationException>(() =>
            dispatcher.Handle<AppReady, AppSnapshot>(_ => Task.FromResult(Snapshot)));
    }

    [Fact]
    public void Handle_RejectsATypeThatIsNotACommand()
    {
        var dispatcher = new BridgeDispatcher(AppOrigin);

        Assert.Throws<InvalidOperationException>(() => dispatcher.Handle<ThemeChanged>(_ => Task.CompletedTask));
    }

    [Fact]
    public void EventMessage_WrapsThePayloadInTheEnvelope()
    {
        string message = BridgeDispatcher.EventMessage(new ThemeChanged(Theme.Light, ThemePreference.Light));

        JsonElement root = Parse(message);
        Assert.Equal("evt", root.GetProperty("t").GetString());
        Assert.Equal("app.theme", root.GetProperty("name").GetString());
        Assert.Equal("light", root.GetProperty("payload").GetProperty("theme").GetString());
        Assert.Equal("light", root.GetProperty("payload").GetProperty("themePreference").GetString());
    }
}
