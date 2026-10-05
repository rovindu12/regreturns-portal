using Microsoft.Extensions.Logging;

using RegReturns.Domain.Submissions;

namespace RegReturns.Application.Returns;

/// <summary>Log events for bank returns (51xx), uploads (52xx) and the workflow (53xx). Never log figures, values, comments or file names.</summary>
internal static partial class ReturnsLog
{
    [LoggerMessage(EventId = 5101, Level = LogLevel.Information,
        Message = "Return draft {SubmissionId} started for obligation {ObligationId} from {Source} with template version {TemplateVersionId}")]
    public static partial void DraftStarted(ILogger logger, Guid submissionId, Guid obligationId, SubmissionSource source, Guid templateVersionId);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Information,
        Message = "Return {SubmissionId} values saved: {ChangedFields} field(s) changed, edit version {EditVersion}")]
    public static partial void ValuesSaved(ILogger logger, Guid submissionId, int changedFields, int editVersion);

    [LoggerMessage(EventId = 5103, Level = LogLevel.Information,
        Message = "Return {SubmissionId} revision {Revision} validated: {Errors} error(s), {Warnings} warning(s), {Unjustified} unjustified")]
    public static partial void Validated(ILogger logger, Guid submissionId, int revision, int errors, int warnings, int unjustified);

    [LoggerMessage(EventId = 5104, Level = LogLevel.Information,
        Message = "Return {SubmissionId} warning {FindingId} ({RuleCode}) justified")]
    public static partial void WarningJustified(ILogger logger, Guid submissionId, Guid findingId, string ruleCode);

    [LoggerMessage(EventId = 5105, Level = LogLevel.Warning,
        Message = "Return {SubmissionId} edit refused: loaded at edit version {Expected}, now at {Actual}")]
    public static partial void EditConflict(ILogger logger, Guid submissionId, int expected, int actual);

    [LoggerMessage(EventId = 5106, Level = LogLevel.Information,
        Message = "Return {SubmissionId} delivered through the API: created {Created}, changed {Changed}, edit version {EditVersion}")]
    public static partial void Delivered(ILogger logger, Guid submissionId, bool created, bool changed, int editVersion);

    [LoggerMessage(EventId = 5107, Level = LogLevel.Warning,
        Message = "API delivery of {ReturnTypeCode} for {Period} refused: {ErrorCode}")]
    public static partial void DeliveryRefused(ILogger logger, string returnTypeCode, string period, string errorCode);

    [LoggerMessage(EventId = 5201, Level = LogLevel.Information,
        Message = "Upload accepted into return {SubmissionId}: {Format}, {SizeBytes} bytes, {FieldCount} field(s), file {StoredFileId}")]
    public static partial void UploadAccepted(ILogger logger, Guid submissionId, ReturnFileFormat format, int sizeBytes, int fieldCount, Guid storedFileId);

    [LoggerMessage(EventId = 5202, Level = LogLevel.Warning,
        Message = "Upload refused for obligation {ObligationId}: {ErrorCode}, {SizeBytes} bytes")]
    public static partial void UploadRefused(ILogger logger, Guid obligationId, string errorCode, int sizeBytes);

    [LoggerMessage(EventId = 5301, Level = LogLevel.Information,
        Message = "Return {SubmissionId} {Action}: {FromStatus} -> {ToStatus}, revision {Revision}, late {IsLate}")]
    public static partial void Transitioned(
        ILogger logger, Guid submissionId, WorkflowAction action, SubmissionStatus fromStatus, SubmissionStatus toStatus, int revision, bool isLate);

    [LoggerMessage(EventId = 5302, Level = LogLevel.Warning,
        Message = "Return {SubmissionId} {Action} refused: {ErrorCode}")]
    public static partial void TransitionRefused(ILogger logger, Guid submissionId, WorkflowAction action, string errorCode);
}
