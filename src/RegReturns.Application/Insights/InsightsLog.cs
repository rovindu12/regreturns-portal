using Microsoft.Extensions.Logging;

using RegReturns.Domain.Insights;

namespace RegReturns.Application.Insights;

/// <summary>Log messages of advisory insights (event ids 5501-5509). Ids, codes and durations only, never figures or text.</summary>
internal static partial class InsightsLog
{
    [LoggerMessage(EventId = 5501, Level = LogLevel.Information,
        Message = "Insight {InsightId} for submission {SubmissionId} revision {Revision} written by {Provider} {Model} in {ElapsedMs} ms (audit entry {AuditSequence})")]
    public static partial void Generated(
        ILogger logger, Guid insightId, Guid submissionId, int revision, InsightProvider provider, string model, long elapsedMs, long auditSequence);

    [LoggerMessage(EventId = 5502, Level = LogLevel.Information,
        Message = "Insight {InsightId} for submission {SubmissionId} revision {Revision} reused: its payload has not changed")]
    public static partial void Reused(ILogger logger, Guid insightId, Guid submissionId, int revision);

    [LoggerMessage(EventId = 5503, Level = LogLevel.Error,
        Message = "Insight payload for submission {SubmissionId} failed the privacy guard and was not sent: {Reason}")]
    public static partial void PayloadRejected(ILogger logger, Guid submissionId, string reason);

    [LoggerMessage(EventId = 5504, Level = LogLevel.Warning,
        Message = "Insight provider {Provider} {Model} gave no answer for submission {SubmissionId} ({ErrorCode}); the rule-based writer answered")]
    public static partial void FellBack(ILogger logger, InsightProvider provider, string model, Guid submissionId, string errorCode);

    [LoggerMessage(EventId = 5505, Level = LogLevel.Warning,
        Message = "Insight for submission {SubmissionId} cancelled by the caller while {Provider} {Model} was answering; the attempt was audited")]
    public static partial void Cancelled(ILogger logger, Guid submissionId, InsightProvider provider, string model);

    [LoggerMessage(EventId = 5506, Level = LogLevel.Warning,
        Message = "Insight request for submission {SubmissionId} refused: {ErrorCode}")]
    public static partial void Refused(ILogger logger, Guid submissionId, string errorCode);
}
