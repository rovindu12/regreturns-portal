using System.Text.Json;

using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RegReturns.Application.Diagnostics;
using RegReturns.Application.Insights;
using RegReturns.Domain.Common;
using RegReturns.Domain.Insights;

namespace RegReturns.Infrastructure.Ai;

/// <summary>
/// Writes insight narratives with Claude through the official Anthropic SDK (ADR 0030): a fixed system prompt, the
/// payload as the only user content, structured JSON output, and a deadline (<c>Ai:Anthropic:TimeoutSeconds</c>) that
/// covers the SDK's retries. Every failure is an <see cref="InsightErrors"/> error, so the rule-based writer answers.
/// </summary>
/// <param name="httpClientFactory">Creates the named client, which the HTTP instrumentation traces.</param>
/// <param name="options">The settings.</param>
/// <param name="timeProvider">The clock behind the deadline.</param>
/// <param name="logger">The logger.</param>
internal sealed class AnthropicInsightNarrator(
    IHttpClientFactory httpClientFactory,
    IOptions<AiOptions> options,
    TimeProvider timeProvider,
    ILogger<AnthropicInsightNarrator> logger) : IInsightNarrator
{
    /// <summary>Name of the <see cref="HttpClient"/> the SDK sends through.</summary>
    public const string HttpClientName = "Anthropic";

    /// <summary>The instructions sent with every payload.</summary>
    internal const string SystemPrompt = """
        You help supervisors at the Bank of Valoria, a fictional central bank, review regulatory returns filed by licensed banks.
        The user message is a JSON document describing one return: each numeric field with its current figure, the bank's approved
        figures for the previous period and for the same period last year, and the percentage changes, followed by the validation
        rules the return failed and whether the bank justified each warning.

        Write a short advisory note for the reviewer:
        - headline: one sentence on what matters most in this return.
        - observations: up to five points on what stands out and what could explain it. Base every point on the figures and rules
          in the document, and never invent figures, events, banks or people. Present causes as possibilities to check, not as
          conclusions.
        - questions: up to five questions the reviewer could put to the bank.

        Refer to fields by their labels and give figures with their units. Write plain text in British English, without markdown.
        If nothing stands out, say so briefly.
        """;

    /// <summary>The JSON schema the answer must follow.</summary>
    internal const string OutputSchemaJson = """
        {
          "type": "object",
          "properties": {
            "headline": { "type": "string" },
            "observations": { "type": "array", "items": { "type": "string" } },
            "questions": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["headline", "observations", "questions"],
          "additionalProperties": false
        }
        """;

    private static readonly Dictionary<string, JsonElement> OutputSchema = ParseSchema(OutputSchemaJson);

    private AnthropicSettings Settings => options.Value.Anthropic;

    /// <inheritdoc />
    public InsightProvider Provider => InsightProvider.Anthropic;

    /// <inheritdoc />
    public string Model => Settings.Model;

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.ApiKey);

    /// <inheritdoc />
    public async Task<Result<NarratorAnswer>> NarrateAsync(InsightRequest request, string payloadJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        if (!IsConfigured)
        {
            AiLog.NotConfigured(logger);
            return InsightErrors.NotConfigured;
        }

        var settings = Settings;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds), timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var started = timeProvider.GetTimestamp();
        using var client = CreateClient(settings);
        try
        {
            var message = await client.Messages.Create(Parameters(settings, payloadJson), linked.Token);
            return Read(message, ElapsedMs(started));
        }
        catch (Exception ex) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested
            && ex is OperationCanceledException or AnthropicIOException)
        {
            AiLog.TimedOut(logger, settings.TimeoutSeconds);
            return InsightErrors.Timeout;
        }
        catch (AnthropicRateLimitException ex)
        {
            return Fail(InsightErrors.RateLimited, ex, started);
        }
        catch (Exception ex) when (ex is AnthropicUnauthorizedException or AnthropicForbiddenException)
        {
            return Fail(InsightErrors.Unauthorized, ex, started);
        }
        catch (Anthropic4xxException ex)
        {
            return Fail(InsightErrors.ProviderRejected, ex, started);
        }
        catch (Exception ex) when (ex is AnthropicApiException or AnthropicIOException or HttpRequestException)
        {
            return Fail(InsightErrors.ProviderUnavailable, ex, started);
        }
        catch (Exception ex) when (ex is AnthropicInvalidDataException or JsonException)
        {
            return Fail(InsightErrors.InvalidOutput, ex, started);
        }
    }

    /// <summary>Builds the request: the system prompt, the payload as the only user content, and the answer's schema.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="payloadJson">The payload.</param>
    /// <returns>The request parameters.</returns>
    internal static MessageCreateParams Parameters(AnthropicSettings settings, string payloadJson) => new()
    {
        Model = settings.Model,
        MaxTokens = settings.MaxOutputTokens,
        System = SystemPrompt,
        Messages = [new() { Role = Role.User, Content = payloadJson }],
        OutputConfig = new OutputConfig
        {
            Effort = EffortOf(settings.Effort),
            Format = new JsonOutputFormat { Schema = OutputSchema },
        },
    };

    private static Effort EffortOf(string effort) => effort switch
    {
        "low" => Anthropic.Models.Messages.Effort.Low,
        "high" => Anthropic.Models.Messages.Effort.High,
        "xhigh" => Anthropic.Models.Messages.Effort.Xhigh,
        "max" => Anthropic.Models.Messages.Effort.Max,
        _ => Anthropic.Models.Messages.Effort.Medium,
    };

    private static Dictionary<string, JsonElement> ParseSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
    }

    private AnthropicClient CreateClient(AnthropicSettings settings)
    {
        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        var timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
        return settings.BaseUrl is { } baseUrl
            ? new AnthropicClient
            {
                ApiKey = settings.ApiKey,
                HttpClient = httpClient,
                Timeout = timeout,
                MaxRetries = settings.MaxRetries,
                BaseUrl = baseUrl.ToString().TrimEnd('/'),
            }
            : new AnthropicClient
            {
                ApiKey = settings.ApiKey,
                HttpClient = httpClient,
                Timeout = timeout,
                MaxRetries = settings.MaxRetries,
            };
    }

    private Result<NarratorAnswer> Read(Message message, long elapsedMs)
    {
        var model = message.Model.Raw();
        var stopReason = message.StopReason?.Raw() ?? "none";
        var inputTokens = message.Usage.InputTokens;
        var outputTokens = message.Usage.OutputTokens;
        RegReturnsTelemetry.AiTokens.Add(inputTokens, new("model", model), new("direction", "input"));
        RegReturnsTelemetry.AiTokens.Add(outputTokens, new("model", model), new("direction", "output"));
        AiLog.Answered(logger, model, elapsedMs, inputTokens, outputTokens, stopReason);

        if (stopReason == "refusal")
        {
            AiLog.Unusable(logger, model, "the model declined");
            return InsightErrors.Refused;
        }

        if (stopReason != "end_turn")
        {
            AiLog.Unusable(logger, model, $"stop reason {stopReason}");
            return InsightErrors.InvalidOutput;
        }

        var text = string.Concat(message.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        Answer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<Answer>(text, InsightJson.Options);
        }
        catch (JsonException)
        {
            answer = null;
        }

        var narrative = answer is null
            ? InsightNarrative.MissingHeadline
            : InsightNarrative.Clean(answer.Headline, answer.Observations, answer.Questions);
        if (narrative.IsFailure)
        {
            AiLog.Unusable(logger, model, "the answer did not match the schema");
            return InsightErrors.InvalidOutput;
        }

        return new NarratorAnswer(narrative.Value, model, inputTokens, outputTokens);
    }

    private Error Fail(Error error, Exception exception, long started)
    {
        var status = exception is AnthropicApiException api ? (int?)api.StatusCode : null;
        AiLog.Failed(logger, ElapsedMs(started), error.Code, status, exception.GetType().Name);
        return error;
    }

    private long ElapsedMs(long started) => (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;

    /// <summary>The answer's shape.</summary>
    /// <param name="Headline">The headline.</param>
    /// <param name="Observations">The observations.</param>
    /// <param name="Questions">The questions.</param>
    private sealed record Answer(string? Headline, IReadOnlyList<string?>? Observations, IReadOnlyList<string?>? Questions);
}
