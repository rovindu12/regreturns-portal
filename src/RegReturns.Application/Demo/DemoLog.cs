using Microsoft.Extensions.Logging;

namespace RegReturns.Application.Demo;

/// <summary>Log messages of the demo reset (event ids 56xx).</summary>
internal static partial class DemoLog
{
    [LoggerMessage(EventId = 5601, Level = LogLevel.Information,
        Message = "Demo reset ({Trigger}) removed {RowsRemoved} rows and seeded {Obligations} obligations and {Submissions} returns in {ElapsedMs} ms (audit entry {AuditSequence})")]
    public static partial void Reset(
        ILogger logger, DemoResetTrigger trigger, int rowsRemoved, int obligations, int submissions, long elapsedMs, long auditSequence);

    [LoggerMessage(EventId = 5602, Level = LogLevel.Warning, Message = "Demo reset ({Trigger}) refused: {ErrorCode}")]
    public static partial void Refused(ILogger logger, DemoResetTrigger trigger, string errorCode);
}
