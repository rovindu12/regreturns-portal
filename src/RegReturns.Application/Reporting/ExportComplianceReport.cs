using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.Logging;

using RegReturns.Application.Auditing;
using RegReturns.Application.Diagnostics;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;

namespace RegReturns.Application.Reporting;

/// <summary>A file format reports export to.</summary>
public enum ReportFormat
{
    /// <summary>An Excel workbook.</summary>
    Xlsx = 1,

    /// <summary>A PDF document (A4 landscape).</summary>
    Pdf = 2,
}

/// <summary>An exported report.</summary>
/// <param name="FileName">The file name to offer, such as <c>compliance-MLR-2026-10-04.xlsx</c>.</param>
/// <param name="ContentType">The media type.</param>
/// <param name="Content">The file's bytes.</param>
public sealed record ReportFile(string FileName, string ContentType, byte[] Content);

/// <summary>Everything an exported compliance report holds.</summary>
/// <param name="Header">What it covers.</param>
/// <param name="Compliance">The grid, totals and overdue list.</param>
/// <param name="GeneratedAt">When it was produced (UTC).</param>
public sealed record ComplianceReportDocument(ReportHeader Header, ComplianceReport Compliance, DateTimeOffset GeneratedAt);

/// <summary>Renders a compliance report as a file. Implemented in Infrastructure with ClosedXML and QuestPDF.</summary>
public interface IComplianceReportRenderer
{
    /// <summary>Renders the report.</summary>
    /// <param name="document">The report.</param>
    /// <param name="format">The file format.</param>
    /// <returns>The file's bytes.</returns>
    byte[] Render(ComplianceReportDocument document, ReportFormat format);
}

/// <summary>
/// Exports the compliance report of one return type (grid, overdue list and every obligation in the window) as Excel or
/// PDF. Every export is recorded in the audit trail with its scope and format, never its contents.
/// </summary>
/// <param name="ReturnTypeCode">The return type's code, or <see langword="null"/> for the first by code.</param>
/// <param name="Format">The file format.</param>
public sealed record ExportComplianceReport(string? ReturnTypeCode, ReportFormat Format);

/// <summary>Handles <see cref="ExportComplianceReport"/> for every portal role.</summary>
/// <param name="builder">Builds the report.</param>
/// <param name="renderer">Writes the file.</param>
/// <param name="auditTrail">Records the export.</param>
/// <param name="auditContext">Who is exporting, from where.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class ExportComplianceReportHandler(
    ReportBuilder builder,
    IComplianceReportRenderer renderer,
    IAuditTrail auditTrail,
    IAuditContext auditContext,
    TimeProvider timeProvider,
    ILogger<ExportComplianceReportHandler> logger) : ICommandHandler<ExportComplianceReport, Result<ReportFile>>
{
    /// <summary>The audit entity type of exported reports.</summary>
    public const string AuditEntityType = "Report";

    /// <summary>The audit entity id of the compliance report.</summary>
    public const string ComplianceReportId = "compliance";

    /// <inheritdoc />
    public async Task<Result<ReportFile>> HandleAsync(ExportComplianceReport command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Enum.IsDefined(command.Format))
        {
            throw new ArgumentOutOfRangeException(nameof(command), command.Format, "Unknown report format.");
        }

        using var activity = RegReturnsTelemetry.ActivitySource.StartActivity("Export compliance report");
        var scopeResult = await builder.ResolveAsync(command.ReturnTypeCode, cancellationToken);
        if (scopeResult.IsFailure)
        {
            activity?.SetStatus(ActivityStatusCode.Error, scopeResult.Error!.Code);
            return scopeResult.Error!;
        }

        var scope = scopeResult.Value;
        var header = scope.Header;
        var started = timeProvider.GetTimestamp();
        var document = new ComplianceReportDocument(header, await builder.ComplianceAsync(scope, cancellationToken), timeProvider.GetUtcNow());
        var file = new ReportFile(
            FileNameOf(header, command.Format),
            ContentTypeOf(command.Format),
            renderer.Render(document, command.Format));

        var format = FormatName(command.Format);
        var details = string.Create(
            CultureInfo.InvariantCulture,
            $"Compliance report {header.ReturnType.Code} {header.PeriodRange}, {(header.AllInstitutions ? "all banks" : "own bank")}, {format}");
        await auditTrail.RecordAsync(
            auditContext.Current.ToRecord(AuditAction.ReportExported, details, AuditEntityType, ComplianceReportId), cancellationToken);

        RegReturnsTelemetry.ReportExports.Add(
            1, new KeyValuePair<string, object?>("report", ComplianceReportId), new KeyValuePair<string, object?>("format", format));
        activity?.SetTag("regreturns.report.format", format);
        var elapsedMs = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
        ReportsLog.Exported(logger, ComplianceReportId, header.ReturnType.Code, format, file.Content.Length, elapsedMs);
        return file;
    }

    /// <summary>Returns the lower-case name of a format, as in file extensions and logs.</summary>
    /// <param name="format">The format.</param>
    /// <returns><c>xlsx</c> or <c>pdf</c>.</returns>
    public static string FormatName(ReportFormat format) => format switch
    {
        ReportFormat.Xlsx => "xlsx",
        ReportFormat.Pdf => "pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown report format."),
    };

    /// <summary>Returns the media type of a format.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The media type.</returns>
    public static string ContentTypeOf(ReportFormat format) => format switch
    {
        ReportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ReportFormat.Pdf => "application/pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown report format."),
    };

    /// <summary>
    /// Returns the file name of an export: <c>compliance-MLR-2026-10-04.pdf</c> for every bank, or with the bank's code
    /// (<c>compliance-MLR-HLB-2026-10-04.pdf</c>) for a bank's own report.
    /// </summary>
    /// <param name="header">What the report covers.</param>
    /// <param name="format">The format.</param>
    /// <returns>The file name.</returns>
    public static string FileNameOf(ReportHeader header, ReportFormat format)
    {
        ArgumentNullException.ThrowIfNull(header);
        var bank = header.Institution is null ? string.Empty : "-" + header.Institution.Code;
        return string.Create(
            CultureInfo.InvariantCulture, $"compliance-{header.ReturnType.Code}{bank}-{header.AsOf:yyyy-MM-dd}.{FormatName(format)}");
    }
}
