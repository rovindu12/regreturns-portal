using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Application.Insights;
using RegReturns.Domain.Common;
using RegReturns.Infrastructure.Ai;
using RegReturns.UnitTests.TestSupport;

namespace RegReturns.UnitTests.Infrastructure.Ai;

public sealed class AnthropicInsightNarratorTests
{
    private const string ApiKey = "sk-ant-test-key";

    private readonly InsightWorld _world = new();
    private readonly FakeTimeProvider _time = new(InsightWorld.Now);

    [Fact]
    public async Task A_good_answer_becomes_the_narrative_with_the_model_and_token_counts()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, Answer(
            """{"headline":"  NPL ratio more than doubled. ","observations":["Check the default.",""],"questions":["Which borrower?"]}""")));

        var answer = (await Narrate(handler)).Value;

        answer.Narrative.Headline.ShouldBe("NPL ratio more than doubled.");
        answer.Narrative.Observations.ShouldBe(["Check the default."]);
        answer.Narrative.Questions.ShouldBe(["Which borrower?"]);
        (answer.Model, answer.InputTokens, answer.OutputTokens).ShouldBe(("claude-opus-5-5", 1234L, 321L));
    }

    [Fact]
    public async Task The_request_carries_the_key_the_instructions_the_payload_and_the_answer_schema()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, Answer("""{"headline":"h","observations":[],"questions":[]}""")));
        var payload = _world.Payload();

        await Narrate(handler, payload: payload);

        var (request, body) = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.ShouldBe("/v1/messages");
        request.Headers.GetValues("x-api-key").ShouldBe([ApiKey]);
        var json = JsonNode.Parse(body)!;
        json["model"]!.GetValue<string>().ShouldBe("claude-opus-5-5");
        json["max_tokens"]!.GetValue<long>().ShouldBe(16000);
        json["system"]!.GetValue<string>().ShouldBe(AnthropicInsightNarrator.SystemPrompt);
        json["messages"]!.AsArray().ShouldHaveSingleItem()!["content"]!.GetValue<string>().ShouldBe(payload);
        json["output_config"]!["effort"]!.GetValue<string>().ShouldBe("medium");
        json["output_config"]!["format"]!["type"]!.GetValue<string>().ShouldBe("json_schema");
        json["output_config"]!["format"]!["schema"]!["required"]!.AsArray().Select(n => n!.GetValue<string>())
            .ShouldBe(["headline", "observations", "questions"]);
    }

    [Fact]
    public async Task The_payload_is_the_only_user_content_and_no_bank_text_is_sent()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, Answer("""{"headline":"h","observations":[],"questions":[]}""")));

        await Narrate(handler, payload: _world.Payload());

        var body = handler.Requests.ShouldHaveSingleItem().Body;
        body.ShouldNotContain(InsightWorld.BankName);
        body.ShouldNotContain(InsightWorld.Justification);
        body.ShouldNotContain(InsightWorld.Remarks);
    }

    [Fact]
    public async Task Without_a_key_nothing_is_sent()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("No request expected."));

        var result = await Narrate(handler, apiKey: null);

        result.Error.ShouldBe(InsightErrors.NotConfigured);
        handler.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limit_error", "Insight.RateLimited")]
    [InlineData(HttpStatusCode.Unauthorized, "authentication_error", "Insight.Unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "permission_error", "Insight.Unauthorized")]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request_error", "Insight.ProviderRejected")]
    [InlineData(HttpStatusCode.InternalServerError, "api_error", "Insight.ProviderUnavailable")]
    [InlineData((HttpStatusCode)529, "overloaded_error", "Insight.ProviderUnavailable")]
    public async Task Provider_errors_become_insight_errors(HttpStatusCode status, string type, string code)
    {
        var handler = new StubHandler((_, _) => Respond(status, $$$"""{"type":"error","error":{"type":"{{{type}}}","message":"no"}}"""));

        var result = await Narrate(handler);

        result.Error.ShouldNotBeNull().Code.ShouldBe(code);
    }

    [Fact]
    public async Task A_network_failure_means_the_provider_is_unavailable()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("Connection refused."));

        (await Narrate(handler)).Error.ShouldBe(InsightErrors.ProviderUnavailable);
    }

    [Fact]
    public async Task No_answer_within_the_timeout_is_a_timeout()
    {
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            received.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });

        var call = Narrate(handler);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(31));

        (await call.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Error.ShouldBe(InsightErrors.Timeout);
    }

    [Fact]
    public async Task A_caller_who_gives_up_gets_the_cancellation()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, cancellationToken) =>
        {
            received.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });

        var call = Narrator(handler).NarrateAsync(_world.Request(), _world.Payload(), caller.Token);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await caller.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_refusal_is_reported_as_such()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, Answer(string.Empty, stopReason: "refusal")));

        (await Narrate(handler)).Error.ShouldBe(InsightErrors.Refused);
    }

    [Fact]
    public async Task An_answer_cut_short_cannot_be_used()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, Answer("""{"headline":"Th""", stopReason: "max_tokens")));

        (await Narrate(handler)).Error.ShouldBe(InsightErrors.InvalidOutput);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"headline":"","observations":[],"questions":[]}""")]
    [InlineData("[]")]
    public async Task An_answer_outside_the_schema_cannot_be_used(string text)
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, Answer(text)));

        (await Narrate(handler)).Error.ShouldBe(InsightErrors.InvalidOutput);
    }

    [Fact]
    public void Parameters_follow_the_settings()
    {
        var settings = new AnthropicSettings { Model = "claude-sonnet-5-5", Effort = "high", MaxOutputTokens = 4096 };

        var parameters = AnthropicInsightNarrator.Parameters(settings, "{}");

        parameters.MaxTokens.ShouldBe(4096);
        JsonSerializer.Serialize(parameters).ShouldContain("claude-sonnet-5-5");
    }

    private Task<Result<NarratorAnswer>> Narrate(StubHandler handler, string? apiKey = ApiKey, string? payload = null) =>
        Narrator(handler, apiKey).NarrateAsync(_world.Request(), payload ?? _world.Payload(), TestContext.Current.CancellationToken);

    private AnthropicInsightNarrator Narrator(StubHandler handler, string? apiKey = ApiKey)
    {
        var options = new AiOptions();
        options.Anthropic.ApiKey = apiKey;
        options.Anthropic.MaxRetries = 0;
        return new AnthropicInsightNarrator(
            new StubHttpClientFactory(handler), Options.Create(options), _time, NullLogger<AnthropicInsightNarrator>.Instance);
    }

    private static string Answer(string text, string stopReason = "end_turn") => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["id"] = "msg_test",
        ["type"] = "message",
        ["role"] = "assistant",
        ["model"] = "claude-opus-5-5",
        ["content"] = new[] { new Dictionary<string, object?> { ["type"] = "text", ["text"] = text, ["citations"] = null } },
        ["stop_reason"] = stopReason,
        ["stop_sequence"] = null,
        ["usage"] = new Dictionary<string, object?>
        {
            ["input_tokens"] = 1234,
            ["output_tokens"] = 321,
            ["cache_creation_input_tokens"] = 0,
            ["cache_read_input_tokens"] = 0,
        },
    });

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string json) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, body));
            return await respond(request, cancellationToken);
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
