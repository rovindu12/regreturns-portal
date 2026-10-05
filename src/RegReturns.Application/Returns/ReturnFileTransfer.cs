using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Diagnostics;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Returns;

/// <summary>
/// Loads an uploaded <c>.xlsx</c> or <c>.csv</c> file into the obligation's return (starting a draft if there is none),
/// keeps the file as evidence and validates. Fields the file leaves out keep their values.
/// </summary>
/// <param name="ObligationId">The obligation id.</param>
/// <param name="FileName">The file name the browser sent.</param>
/// <param name="Content">The file content.</param>
public sealed record UploadReturnFile(Guid ObligationId, string FileName, byte[] Content);

/// <summary>What an upload did.</summary>
/// <param name="SubmissionId">The return the values went into.</param>
/// <param name="FieldsLoaded">How many fields the file set.</param>
/// <param name="FieldsInTemplate">How many fields the return has.</param>
/// <param name="Validation">The validation outcome after loading.</param>
public sealed record UploadOutcome(Guid SubmissionId, int FieldsLoaded, int FieldsInTemplate, ValidationOutcome Validation);

/// <summary>Handles <see cref="UploadReturnFile"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
/// <param name="reader">Checks and parses the file.</param>
/// <param name="validator">Runs the validation engine.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class UploadReturnFileHandler(
    IAppDbContext db,
    ICurrentActor currentActor,
    IReturnFileReader reader,
    ReturnValidator validator,
    TimeProvider timeProvider,
    ILogger<UploadReturnFileHandler> logger) : ICommandHandler<UploadReturnFile, Result<UploadOutcome>>
{
    private const int MaxCodesInMessage = 10;

    /// <inheritdoc />
    public async Task<Result<UploadOutcome>> HandleAsync(UploadReturnFile command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = await UploadAsync(command, cancellationToken);
        if (result.IsFailure)
        {
            ReturnsLog.UploadRefused(logger, command.ObligationId, result.Error!.Code, command.Content.Length);
            RegReturnsTelemetry.Uploads.Add(
                1, new KeyValuePair<string, object?>("outcome", "refused"), new KeyValuePair<string, object?>("reason", result.Error.Code));
        }
        else
        {
            RegReturnsTelemetry.Uploads.Add(1, new KeyValuePair<string, object?>("outcome", "accepted"));
        }

        return result;
    }

    private async Task<Result<UploadOutcome>> UploadAsync(UploadReturnFile command, CancellationToken cancellationToken)
    {
        var actor = await BankReturnAccess.BankActorAsync(currentActor, cancellationToken);
        if (actor.IsFailure)
        {
            return actor.Error!;
        }

        if (command.Content.Length == 0)
        {
            return UploadErrors.Empty;
        }

        if (command.Content.Length > StoredFile.MaxSizeBytes)
        {
            return UploadErrors.TooLarge;
        }

        var file = reader.Read(command.FileName, command.Content);
        if (file.IsFailure)
        {
            return file.Error!;
        }

        var now = timeProvider.GetUtcNow();
        var opened = await ReturnDraftFactory.OpenAsync(db, actor.Value, command.ObligationId, SubmissionSource.Upload, now, cancellationToken);
        if (opened.IsFailure)
        {
            return opened.Error!;
        }

        var (submission, created) = opened.Value;
        if (!submission.IsEditable)
        {
            return SubmissionErrors.NotEditable;
        }

        var template = await BankReturnAccess.LoadTemplateAsync(db, submission.TemplateVersionId, cancellationToken);
        var unknown = file.Value.Values.Select(v => v.Key).Where(code => template.FindField(code) is null).ToList();
        if (unknown.Count > 0)
        {
            var listed = string.Join(", ", unknown.Take(MaxCodesInMessage)) + (unknown.Count > MaxCodesInMessage ? ", ..." : string.Empty);
            return UploadErrors.UnknownFields.WithMessage(
                $"The file has field codes that are not in this return: {listed}. Download the template to see the codes.");
        }

        var values = file.Value.Values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
        var set = submission.SetValues(template, values, actor.Value, now);
        if (set.IsFailure)
        {
            return set.Error!;
        }

        var stored = StoredFile.Create(submission, command.FileName, file.Value.ContentType, command.Content, actor.Value, now);
        await db.StoredFiles.AddAsync(stored, cancellationToken);
        var outcome = await validator.ValidateAsync(submission, template, cancellationToken);
        if (outcome.IsFailure)
        {
            return outcome.Error!;
        }

        var saved = await db.SaveOrConflictAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        if (created)
        {
            ReturnsLog.DraftStarted(logger, submission.Id, submission.ObligationId, submission.Source, submission.TemplateVersionId);
        }

        ReturnsLog.UploadAccepted(logger, submission.Id, file.Value.Format, stored.SizeBytes, values.Count, stored.Id);
        return new UploadOutcome(submission.Id, values.Count, template.Fields.Count, outcome.Value);
    }
}

/// <summary>
/// Downloads an obligation's return as a file to fill in: the template version that applies (or the one the return was
/// started with), with any values already entered.
/// </summary>
/// <param name="ObligationId">The obligation id.</param>
/// <param name="Format">The file format.</param>
public sealed record DownloadReturnFile(Guid ObligationId, ReturnFileFormat Format);

/// <summary>A file to send to the browser.</summary>
/// <param name="FileName">The download file name.</param>
/// <param name="ContentType">The MIME type.</param>
/// <param name="Content">The content.</param>
public sealed record FileDownload(string FileName, string ContentType, byte[] Content);

/// <summary>Handles <see cref="DownloadReturnFile"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
/// <param name="writer">Writes the file.</param>
public sealed class DownloadReturnFileHandler(IAppDbContext db, ICurrentActor currentActor, IReturnFileWriter writer)
    : IQueryHandler<DownloadReturnFile, Result<FileDownload>>
{
    /// <inheritdoc />
    public async Task<Result<FileDownload>> HandleAsync(DownloadReturnFile query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var actor = await BankReturnAccess.BankActorAsync(currentActor, cancellationToken);
        if (actor.IsFailure)
        {
            return actor.Error!;
        }

        var context = await db.Obligations.AsNoTracking()
            .Where(o => o.Id == query.ObligationId && o.InstitutionId == actor.Value.InstitutionId)
            .Join(db.ReturnTypes, o => o.ReturnTypeId, r => r.Id, (o, r) => new { Obligation = o, ReturnType = r })
            .Join(db.Institutions, x => x.Obligation.InstitutionId, i => i.Id, (x, i) => new { x.Obligation, x.ReturnType, i.Code, i.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (context is null)
        {
            return SubmissionErrors.NotFound;
        }

        var liveId = await BankReturnAccess.LiveSubmissionIdAsync(db, query.ObligationId, cancellationToken);
        var live = liveId is { } id
            ? await db.Submissions.AsNoTracking().Include(s => s.Values).SingleAsync(s => s.Id == id, cancellationToken)
            : null;

        TemplateVersion? template;
        if (live is not null)
        {
            template = await BankReturnAccess.LoadTemplateAsync(db, live.TemplateVersionId, cancellationToken);
        }
        else
        {
            var versions = await db.TemplateVersions.AsNoTracking()
                .Include(v => v.Fields)
                .Where(v => v.ReturnTypeId == context.ReturnType.Id && v.Status == TemplateStatus.Published)
                .ToListAsync(cancellationToken);
            template = TemplateVersion.SelectFor(versions, context.Obligation.Period);
        }

        if (template is null)
        {
            return TemplateErrors.NoApplicableVersion;
        }

        var period = context.Obligation.Period.Label;
        var sheet = new ReturnFileSheet(
            $"{context.ReturnType.Name} ({context.ReturnType.Code}), template version {template.Version}",
            string.Create(CultureInfo.InvariantCulture, $"{context.Name} ({context.Code}), period {period}, due {context.Obligation.DueDate:yyyy-MM-dd}"),
            [.. template.Fields.OrderBy(f => f.DisplayOrder).Select(f => new ReturnFileRow(
                f.Code, f.Label, f.Section, f.DataType, f.Unit, f.Precision, live?.FindValue(f.Code)?.RawValue))]);
        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"{context.Code}_{context.ReturnType.Code}_{period}_v{template.Version}{ReturnFileFormats.ExtensionOf(query.Format)}");
        return new FileDownload(fileName, ReturnFileFormats.ContentTypeOf(query.Format), writer.Write(query.Format, sheet));
    }
}
