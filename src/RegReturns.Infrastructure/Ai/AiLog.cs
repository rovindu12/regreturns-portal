using Microsoft.Extensions.Logging;

namespace RegReturns.Infrastructure.Ai;

/// <summary>
/// Log messages of the AI provider (event ids 5511-5519). Models, token counts, status codes and durations only: never
/// the payload, the answer or the API key.
/// </summary>
internal static partial class AiLog
{
    [LoggerMessage(EventId = 5511, Level = LogLevel.Information,
        Message = "Anthropic answered with {Model} in {ElapsedMs} ms: {InputTokens} input and {OutputTokens} output tokens, stop reason {StopReason}")]
    public static partial void Answered(ILogger logger, string model, long elapsedMs, long inputTokens, long outputTokens, string stopReason);

    [LoggerMessage(EventId = 5512, Level = LogLevel.Warning,
        Message = "Anthropic call failed after {ElapsedMs} ms with {ErrorCode} (HTTP {StatusCode}, {ExceptionType})")]
    public static partial void Failed(ILogger logger, long elapsedMs, string errorCode, int? statusCode, string exceptionType);

    [LoggerMessage(EventId = 5513, Level = LogLevel.Warning,
        Message = "Anthropic did not answer within {TimeoutSeconds} s")]
    public static partial void TimedOut(ILogger logger, int timeoutSeconds);

    [LoggerMessage(EventId = 5514, Level = LogLevel.Warning,
        Message = "Anthropic answer from {Model} could not be used: {Reason}")]
    public static partial void Unusable(ILogger logger, string model, string reason);

    [LoggerMessage(EventId = 5515, Level = LogLevel.Information,
        Message = "No Anthropic API key is configured, so the rule-based writer answers")]
    public static partial void NotConfigured(ILogger logger);
}
