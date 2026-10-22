using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using NAudio.Wave;
using VoiceCtrl.Core.Config;
using VoiceCtrl.Core.Online;
using VoiceCtrl.Core.Rewrite;
using Xunit;

namespace VoiceCtrl.Core.Tests.Online;

/// <summary>Ticket 60: the OpenAI-compatible wire format, the audio and text-only paths, model backups,
/// long clips, and the key check.</summary>
public sealed class CompatibleClientTests
{
    private const string Key = "nvapi-0000000000000000000000000000000000000000000000000000000000000000";

    private static readonly AppConfig Config = ConfigLoader.Load(new UserSettings(), "");

    private static OnlineEndpoint Nvidia => OnlinePresets.For(OnlineProvider.Nvidia, new OtherProviderSettings());

    [Fact]
    public void Nvidia_HearsAClip_OnlyAsAnAudioUrlDataUri()
    {
        JsonObject body = ChatCompletions.Body(Nvidia, Nvidia.AudioModel, [ChatMessage.User("Transcribe.", [1, 2, 3])]);

        JsonNode part = body["messages"]![0]!["content"]![1]!;
        Assert.Equal("audio_url", part["type"]!.GetValue<string>());
        Assert.Equal("data:audio/wav;base64,AQID", part["audio_url"]!["url"]!.GetValue<string>());
        Assert.False(body["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
        Assert.Null(body["messages"]![0]!["content"]![1]!["input_audio"]);
    }

    [Fact]
    public void OpenAi_HearsAClip_AsInputAudio_WithNoTemperature_AndALowReasoningEffortForText()
    {
        OnlineEndpoint openAi = OnlinePresets.For(OnlineProvider.OpenAi, new OtherProviderSettings());

        JsonObject audio = ChatCompletions.Body(openAi, openAi.AudioModel, [ChatMessage.User("Transcribe.", [1, 2, 3])]);
        JsonObject text = ChatCompletions.Body(openAi, openAi.TextModel, [ChatMessage.User("Rewrite.")]);

        JsonNode part = audio["messages"]![0]!["content"]![1]!;
        Assert.Equal("input_audio", part["type"]!.GetValue<string>());
        Assert.Equal("wav", part["input_audio"]!["format"]!.GetValue<string>());
        Assert.Null(audio["temperature"]);
        Assert.Null(audio["reasoning_effort"]);
        Assert.Equal("low", text["reasoning_effort"]!.GetValue<string>());
    }

    [Fact]
    public async Task AModelThatHearsAudio_IsSentTheClipWithTheOnlinePrompt_AndItsAnswerIsPasted()
    {
        var server = new FakeServer(_ => Reply("Book the table for Friday."));
        using var client = new CompatibleTranscriptionClient(
            Config, () => Nvidia, () => Key, localSpeech: null, foregroundProcessName: () => "notepad", handler: server);

        string? text = await client.TranscribeAsync(Wav(TimeSpan.FromSeconds(2)));

        Assert.Equal("Book the table for Friday.", text);
        JsonObject request = Assert.Single(server.Bodies);
        Assert.Equal(OnlinePresets.NvidiaAudioModel, request["model"]!.GetValue<string>());
        Assert.Contains("dictation transcription engine", request["messages"]![0]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("Bearer " + Key, server.Authorization);
    }

    [Fact]
    public async Task ATextOnlyModel_CleansWhatThePcsSpeechModelHeard_AndIsSentNoAudio()
    {
        var server = new FakeServer(_ => Reply("Book the table for Friday."));
        var speech = new FakeSpeech("um book the table for uh friday");
        OnlineEndpoint textOnly = OnlinePresets.For(OnlineProvider.Other, new OtherProviderSettings("https://example.test/v1", "text-model", AudioInput.None));
        using var client = new CompatibleTranscriptionClient(
            Config, () => textOnly, () => Key, speech, foregroundProcessName: () => "notepad", handler: server);

        string? text = await client.TranscribeAsync(Wav(TimeSpan.FromSeconds(2)));

        Assert.Equal("Book the table for Friday.", text);
        Assert.Equal(1, speech.Calls);
        JsonObject request = Assert.Single(server.Bodies);
        Assert.DoesNotContain("audio", request.ToJsonString());
        Assert.Contains("<transcript>\num book the table for uh friday\n</transcript>", request["messages"]![1]!["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task TwoSteps_TheAudioModelWritesTheWordsDown_ThenTheTextModelCleansThem()
    {
        var server = new FakeServer(body => body["model"]!.GetValue<string>() == OnlinePresets.NvidiaAudioModel
            ? Reply("Um book the table for uh Friday.")
            : Reply("Book the table for Friday."));
        OnlineEndpoint twoStep = Nvidia with { TwoStep = true };
        using var client = new CompatibleTranscriptionClient(Config, () => twoStep, () => Key, foregroundProcessName: () => "notepad", handler: server);

        Assert.Equal("Book the table for Friday.", await client.TranscribeAsync(Wav(TimeSpan.FromSeconds(2))));
        Assert.Equal([OnlinePresets.NvidiaAudioModel, OnlinePresets.NvidiaTextModel], server.Bodies.Select(b => b["model"]!.GetValue<string>()));
    }

    [Fact]
    public async Task ABusyPrimaryModel_HandsTheJobToTheProvidersBackupModel()
    {
        var server = new FakeServer(body => body["model"]!.GetValue<string>() == OnlinePresets.NvidiaTextModel
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("""{"error":{"message":"Worker local total request limit reached (16/16)"}}""") }
            : Reply("Dear Mark,"));
        using var writer = new CompatibleEmailBodyWriter(() => Nvidia, () => Key, server);

        string? body = await ((VoiceCtrl.Core.EmailRequests.IEmailBodyWriter)writer).WriteBodyAsync(
            VoiceCtrl.Core.EmailRequests.EmailRequest.FromInstruction("write an email to Mark thanking him")!, [], CancellationToken.None);

        Assert.Equal("Dear Mark,", body);
        Assert.Equal([OnlinePresets.NvidiaTextModel, OnlinePresets.NvidiaTextBackup], server.Bodies.Select(b => b["model"]!.GetValue<string>()));
    }

    [Fact]
    public async Task APrimaryModelThatGivesNoFirstAnswerInTime_HandsOverToTheBackup()
    {
        List<string> tried = [];
        string answer = await ModelAttempts.RunAsync(["slow", "quick"], TimeSpan.FromMilliseconds(50), async (model, token) =>
        {
            tried.Add(model);
            if (model == "slow")
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
            }

            return model;
        }, "test", CancellationToken.None);

        Assert.Equal("quick", answer);
        Assert.Equal(["slow", "quick"], tried);
    }

    [Fact]
    public async Task ARewriteOnNvidia_IsHeardByTheOmniModel_AndAppliedByTheTextModel()
    {
        var server = new FakeServer(body => body["model"]!.GetValue<string>() == OnlinePresets.NvidiaAudioModel
            ? Reply("Make it shorter.")
            : Reply("See you Friday."));
        using var client = new CompatibleRewriteClient(() => Nvidia, () => Key, null, null, server);

        RewriteOutcome outcome = await client.RewriteAsync([1, 2, 3], "I will see you on Friday then, all being well.", CancellationToken.None);

        Assert.Equal("See you Friday.", outcome.Text);
        Assert.Equal([OnlinePresets.NvidiaAudioModel, OnlinePresets.NvidiaTextModel], server.Bodies.Select(b => b["model"]!.GetValue<string>()));
    }

    [Fact]
    public void ALongRecording_IsCutAtItsQuietestMoments_IntoPiecesAProviderTakes()
    {
        byte[] wav = Wav(TimeSpan.FromSeconds(25), silenceAt: [TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(18)]);

        IReadOnlyList<byte[]> pieces = AudioChunks.Split(wav, TimeSpan.FromSeconds(10));

        Assert.Equal(3, pieces.Count);
        TimeSpan first = Duration(pieces[0]);
        Assert.InRange(first.TotalSeconds, 8.9, 9.6);
        Assert.Equal(25, pieces.Sum(p => Duration(p).TotalSeconds), 1);
        Assert.Single(AudioChunks.Split(Wav(TimeSpan.FromSeconds(5)), TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData("Seven, three, nine.", true)]
    [InlineData("7 3 9", true)]
    [InlineData("I'm sorry, but I can't access the attached audio.", false)]
    public void TheAudioProbe_CountsAModelThatWritesTheDigitsBack_AsHearing(string reply, bool hears)
    {
        Assert.Equal(hears, OnlineKeyValidator.HeardTheDigits(reply));
    }

    [Fact]
    public async Task TheKeyCheck_ForOther_FindsTheShapeTheModelHears()
    {
        // A model like NVIDIA's omni: it answers input_audio as if nothing were attached.
        var server = new FakeServer(body => body.ToJsonString().Contains("audio_url")
            ? Reply("Seven, three, nine.")
            : body.ToJsonString().Contains("input_audio") ? Reply("I can't access the audio.") : Reply("OK"));
        using var http = new HttpClient(server);
        OnlineEndpoint other = OnlinePresets.For(OnlineProvider.Other, new OtherProviderSettings("https://example.test/v1", "omni"));

        KeyCheckOutcome outcome = await OnlineKeyValidator.CheckAsync(other, Key, http);

        Assert.Equal(KeyValidation.Valid, outcome.Result);
        Assert.Equal(AudioInput.AudioUrl, outcome.Audio);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, KeyValidation.InvalidKey)]
    [InlineData(HttpStatusCode.Unauthorized, KeyValidation.InvalidKey)]
    [InlineData(HttpStatusCode.ServiceUnavailable, KeyValidation.Unknown)]
    public async Task TheKeyCheck_OnNvidia_AsksTheModelForOneWord(HttpStatusCode status, KeyValidation expected)
    {
        var server = new FakeServer(_ => new HttpResponseMessage(status) { Content = new StringContent("{}") });
        using var http = new HttpClient(server);

        KeyCheckOutcome outcome = await OnlineKeyValidator.CheckAsync(Nvidia, Key, http);

        Assert.Equal(expected, outcome.Result);
        Assert.Contains("/chat/completions", server.Paths.Single());
    }

    private static HttpResponseMessage Reply(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(new JsonObject
        {
            ["choices"] = new JsonArray { new JsonObject { ["message"] = new JsonObject { ["content"] = text } } },
        }.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    /// <summary>A 16 kHz 16-bit mono WAV of a steady tone, quiet at <paramref name="silenceAt"/> for half a second.</summary>
    private static byte[] Wav(TimeSpan length, TimeSpan[]? silenceAt = null)
    {
        var format = new WaveFormat(16000, 16, 1);
        using var stream = new MemoryStream();
        using (var writer = new WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(stream), format))
        {
            int samples = (int)(length.TotalSeconds * 16000);
            for (int i = 0; i < samples; i++)
            {
                double t = i / 16000.0;
                bool quiet = silenceAt?.Any(s => t >= s.TotalSeconds - 0.25 && t < s.TotalSeconds + 0.25) == true;
                short value = quiet ? (short)0 : (short)(8000 * Math.Sin(2 * Math.PI * 440 * t));
                writer.WriteSample(value / 32768f);
            }
        }

        return stream.ToArray();
    }

    private static TimeSpan Duration(byte[] wav)
    {
        using var reader = new WaveFileReader(new MemoryStream(wav));
        return reader.TotalTime;
    }

    private sealed class FakeSpeech(string words) : IInstructionTranscriber
    {
        public int Calls { get; private set; }

        public Task<string?> TranscribeInstructionAsync(byte[] wavBytes, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<string?>(words);
        }

        public Task PrewarmAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeServer(Func<JsonObject, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<JsonObject> Bodies { get; } = [];

        public List<string> Paths { get; } = [];

        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Authorization = request.Headers.Authorization?.ToString() ?? request.Headers.GetValues("Authorization").FirstOrDefault();
            JsonObject body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            Bodies.Add(body);
            return respond(body);
        }
    }
}
