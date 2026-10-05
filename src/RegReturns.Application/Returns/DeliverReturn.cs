using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Diagnostics;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;
using RegReturns.Application.Templates;
using RegReturns.Domain.Common;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;

namespace RegReturns.Application.Returns;

/// <summary>
/// Delivers a complete return from a bank system (ADR 0026): opens the obligation's live return, or starts a draft
/// with the template version that applies, replaces all its values (fields left out become blank) and validates.
/// Findings are part of the outcome, not a refusal; a bank checker submits the return in the portal.
/// </summary>
/// <param name="ReturnTypeCode">The return type code (case-insensitive).</param>
/// <param name="Period">The reporting period.</param>
/// <param name="Values">Raw values by field code; <see langword="null"/> or blank means no value.</param>
public sealed record DeliverReturn(string ReturnTypeCode, ReportingPeriod Period, IReadOnlyDictionary<string, string?> Values);

/// <summary>What a delivery did.</summary>
/// <param name="SubmissionId">The return the values went into.</param>
/// <param name="Created">Whether a new draft was started (otherwise the live return was updated).</param>
/// <param name="Changed">Whether any value changed.</param>
/// <param name="Status">The return's status.</param>
/// <param name="EditVersion">The return's edit counter after the delivery.</param>
/// <param name="Validation">The findings after validation.</param>
public sealed record DeliveryOutcome(
    Guid SubmissionId, bool Created, bool Changed, SubmissionStatus Status, int EditVersion, SubmissionValidation Validation);

/// <summary>Refusals of a delivery that are not about the return's state.</summary>
public static class DeliveryErrors
{
    /// <summary>The bank has no filing obligation for the return type and period.</summary>
    public static readonly Error NoObligation = new(
        "Delivery.NoObligation", "Your bank has no filing obligation for this return type and period.");

    /// <summary>A value is keyed by a code that is not a field of the return.</summary>
    public static readonly Error UnknownFields = new(
        "Delivery.UnknownFields", "Some values are keyed by codes that are not fields of this return.");

    /// <summary>Another delivery or edit of the same return was saved first.</summary>
    public static readonly Error Concurrent = new(
        "Delivery.Concurrent", "The return changed while this delivery was being saved. Send it again.");
}

/// <summary>Handles <see cref="DeliverReturn"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller (a client user in the API).</param>
/// <param name="validator">Runs the validation engine.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class DeliverReturnHandler(
    IAppDbContext db,
    ICurrentActor currentActor,
    ReturnValidator validator,
    TimeProvider timeProvider,
    ILogger<DeliverReturnHandler> logger) : ICommandHandler<DeliverReturn, Result<DeliveryOutcome>>
{
    private const int MaxCodesInMessage = 10;

    /// <inheritdoc />
    public async Task<Result<DeliveryOutcome>> HandleAsync(DeliverReturn command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var result = await DeliverAsync(command, cancellationToken);
        if (result.IsFailure)
        {
            ReturnsLog.DeliveryRefused(logger, command.ReturnTypeCode, command.Period.Label, result.Error!.Code);
            RegReturnsTelemetry.Deliveries.Add(
                1,
                new KeyValuePair<string, object?>(RegReturnsTelemetry.OutcomeTag, "refused"),
                new KeyValuePair<string, object?>(RegReturnsTelemetry.ErrorCodeTag, result.Error.Code));
        }
        else
        {
            RegReturnsTelemetry.Deliveries.Add(
                1, new KeyValuePair<string, object?>(RegReturnsTelemetry.OutcomeTag, result.Value.Created ? "created" : "updated"));
        }

        return result;
    }

    private async Task<Result<DeliveryOutcome>> DeliverAsync(DeliverReturn command, CancellationToken cancellationToken)
    {
        var actor = await BankReturnAccess.BankActorAsync(currentActor, cancellationToken);
        if (actor.IsFailure)
        {
            return actor.Error!;
        }

        var code = command.ReturnTypeCode.Trim().ToUpperInvariant();
        var returnType = await db.ReturnTypes.AsNoTracking().SingleOrDefaultAsync(r => r.Code == code && r.IsActive, cancellationToken);
        if (returnType is null)
        {
            return ReturnTypeErrors.NotFound;
        }

        var period = command.Period;
        if (period.Frequency != returnType.Frequency)
        {
            return ReturnTypeErrors.PeriodMismatchFor(returnType.Code, returnType.Frequency);
        }

        var obligationId = await db.Obligations.AsNoTracking()
            .Where(o => o.InstitutionId == actor.Value.InstitutionId
                && o.ReturnTypeId == returnType.Id
                && o.Period.Frequency == period.Frequency
                && o.Period.Year == period.Year
                && o.Period.Number == period.Number)
            .Select(o => (Guid?)o.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (obligationId is null)
        {
            return DeliveryErrors.NoObligation;
        }

        var now = timeProvider.GetUtcNow();
        var opened = await ReturnDraftFactory.OpenAsync(db, actor.Value, obligationId.Value, SubmissionSource.Api, now, cancellationToken);
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
        var unknown = command.Values.Keys.Where(c => template.FindField(c) is null).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            var listed = string.Join(", ", unknown.Take(MaxCodesInMessage)) + (unknown.Count > MaxCodesInMessage ? ", ..." : string.Empty);
            return DeliveryErrors.UnknownFields.WithMessage(
                $"These codes are not fields of {returnType.Code} template version {template.Version}: {listed}.");
        }

        // A delivery is the whole return: a field it leaves out is blank, so stale values never survive.
        var values = template.Fields.ToDictionary(f => f.Code, f => command.Values.GetValueOrDefault(f.Code), StringComparer.Ordinal);
        var editVersion = submission.EditVersion;
        var set = submission.SetValues(template, values, actor.Value, now);
        if (set.IsFailure)
        {
            return set.Error!;
        }

        var outcome = await validator.ValidateAsync(submission, template, cancellationToken);
        if (outcome.IsFailure)
        {
            return outcome.Error!;
        }

        try
        {
            var saved = await db.SaveOrConflictAsync(cancellationToken);
            if (saved.IsFailure)
            {
                return DeliveryErrors.Concurrent;
            }
        }
        catch (DbUpdateException) when (created)
        {
            // Another request started the obligation's live return first; the unique index refused this draft.
            return DeliveryErrors.Concurrent;
        }

        if (created)
        {
            ReturnsLog.DraftStarted(logger, submission.Id, submission.ObligationId, submission.Source, submission.TemplateVersionId);
        }

        var changed = submission.EditVersion != editVersion;
        ReturnsLog.Delivered(logger, submission.Id, created, changed, submission.EditVersion);
        return new DeliveryOutcome(
            submission.Id, created, changed, submission.Status, submission.EditVersion, SubmissionValidation.Of(submission, template));
    }
}
