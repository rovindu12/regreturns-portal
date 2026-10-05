using Microsoft.Extensions.Logging;

namespace RegReturns.Application.Reporting;

/// <summary>Log messages of reports (event ids 54xx). They name reports, scopes and formats, never figures.</summary>
internal static partial class ReportsLog
{
    [LoggerMessage(EventId = 5401, Level = LogLevel.Information,
        Message = "Report {Report} for {ReturnTypeCode} exported as {Format}: {SizeBytes} bytes in {ElapsedMs} ms")]
    public static partial void Exported(ILogger logger, string report, string returnTypeCode, string format, int sizeBytes, long elapsedMs);
}
