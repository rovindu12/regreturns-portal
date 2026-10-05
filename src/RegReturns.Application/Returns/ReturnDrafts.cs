using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Returns;

/// <summary>
/// Opens the return for an obligation of the caller's bank: its live submission if there is one, otherwise a new
/// draft captured with the template version that applies to the period (ADR 0009). Only makers can start a draft.
/// </summary>
/// <param name="ObligationId">The obligation id.</param>
public sealed record StartReturnDraft(Guid ObligationId);

/// <summary>Handles <see cref="StartReturnDraft"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class StartReturnDraftHandler(
    IAppDbContext db, ICurrentActor currentActor, TimeProvider timeProvider, ILogger<StartReturnDraftHandler> logger)
    : ICommandHandler<StartReturnDraft, Result<Guid>>
{
    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(StartReturnDraft command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var actor = await BankReturnAccess.BankActorAsync(currentActor, cancellationToken);
        if (actor.IsFailure)
        {
            return actor.Error!;
        }

        var opened = await ReturnDraftFactory.OpenAsync(
            db, actor.Value, command.ObligationId, SubmissionSource.Web, timeProvider.GetUtcNow(), cancellationToken);
        if (opened.IsFailure)
        {
            return opened.Error!;
        }

        var (submission, created) = opened.Value;
        if (!created)
        {
            return submission.Id;
        }

        try
        {
            var saved = await db.SaveOrConflictAsync(cancellationToken);
            if (saved.IsFailure)
            {
                return saved.Error!;
            }
        }
        catch (DbUpdateException)
        {
            // Another request (a double click) created the live submission first; the unique index refused this one.
            var winner = await BankReturnAccess.LiveSubmissionIdAsync(db, command.ObligationId, cancellationToken);
            return winner is { } id ? id : throw new InvalidOperationException("A draft could not be saved and none exists.");
        }

        ReturnsLog.DraftStarted(logger, submission.Id, submission.ObligationId, submission.Source, submission.TemplateVersionId);
        return submission.Id;
    }
}

/// <summary>Finds or creates the live submission of an obligation; shared by draft start and upload.</summary>
internal static class ReturnDraftFactory
{
    /// <summary>
    /// Returns the obligation's live submission (tracked, with values and findings) or adds a new draft to the unit of
    /// work. The caller saves.
    /// </summary>
    public static async Task<Result<(Submission Submission, bool Created)>> OpenAsync(
        IAppDbContext db, Actor actor, Guid obligationId, SubmissionSource source, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var obligation = await db.Obligations
            .SingleOrDefaultAsync(o => o.Id == obligationId && o.InstitutionId == actor.InstitutionId, cancellationToken);
        if (obligation is null)
        {
            return SubmissionErrors.NotFound;
        }

        if (await BankReturnAccess.LiveSubmissionIdAsync(db, obligationId, cancellationToken) is { } liveId)
        {
            var live = await BankReturnAccess.LoadSubmissionAsync(db, actor, liveId, cancellationToken);
            return (live!, false);
        }

        if (obligation.Status == ObligationStatus.Fulfilled)
        {
            return SubmissionErrors.ObligationClosed;
        }

        var versions = await db.TemplateVersions.AsNoTracking()
            .Where(v => v.ReturnTypeId == obligation.ReturnTypeId && v.Status == TemplateStatus.Published)
            .ToListAsync(cancellationToken);
        var template = TemplateVersion.SelectFor(versions, obligation.Period);
        if (template is null)
        {
            return TemplateErrors.NoApplicableVersion;
        }

        var draft = Submission.CreateDraft(obligation, template, actor, source, now);
        if (draft.IsFailure)
        {
            return draft.Error!;
        }

        await db.Submissions.AddAsync(draft.Value, cancellationToken);
        return (draft.Value, true);
    }
}

/// <summary>
/// Saves values entered on the form and validates them in the same step, so the maker sees findings straight away.
/// Refused if someone changed the return after the form was loaded (<paramref name="ExpectedEditVersion"/>).
/// </summary>
/// <param name="SubmissionId">The submission id.</param>
/// <param name="ExpectedEditVersion">The <c>EditVersion</c> the form was loaded with.</param>
/// <param name="Values">Raw values by field code; blank means no value.</param>
public sealed record SaveReturnValues(Guid SubmissionId, int ExpectedEditVersion, IReadOnlyDictionary<string, string?> Values);

/// <summary>Handles <see cref="SaveReturnValues"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
/// <param name="validator">Runs the validation engine.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class SaveReturnValuesHandler(
    IAppDbContext db,
    ICurrentActor currentActor,
    ReturnValidator validator,
    TimeProvider timeProvider,
    ILogger<SaveReturnValuesHandler> logger) : ICommandHandler<SaveReturnValues, Result<ValidationOutcome>>
{
    /// <inheritdoc />
    public async Task<Result<ValidationOutcome>> HandleAsync(SaveReturnValues command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var actor = await BankReturnAccess.BankActorAsync(currentActor, cancellationToken);
        if (actor.IsFailure)
        {
            return actor.Error!;
        }

        var submission = await BankReturnAccess.LoadSubmissionAsync(db, actor.Value, command.SubmissionId, cancellationToken);
        if (submission is null)
        {
            return SubmissionErrors.NotFound;
        }

        if (submission.EditVersion != command.ExpectedEditVersion)
        {
            ReturnsLog.EditConflict(logger, submission.Id, command.ExpectedEditVersion, submission.EditVersion);
            return SubmissionErrors.EditConflict;
        }

        var template = await BankReturnAccess.LoadTemplateAsync(db, submission.TemplateVersionId, cancellationToken);
        var before = submission.Values.ToDictionary(v => v.FieldCode, v => v.RawValue, StringComparer.Ordinal);
        var set = submission.SetValues(template, command.Values, actor.Value, timeProvider.GetUtcNow());
        if (set.IsFailure)
        {
            return set.Error!;
        }

        var outcome = await validator.ValidateAsync(submission, template, cancellationToken);
        if (outcome.IsFailure)
        {
            return outcome;
        }

        var saved = await db.SaveOrConflictAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        var changed = submission.Values.Count(v => !before.TryGetValue(v.FieldCode, out var old) || !string.Equals(old, v.RawValue, StringComparison.Ordinal));
        ReturnsLog.ValuesSaved(logger, submission.Id, changed, submission.EditVersion);
        return outcome;
    }
}

/// <summary>Runs validation again, for example after last period's return was approved (variance rules use it).</summary>
/// <param name="SubmissionId">The submission id.</param>
public sealed record ValidateReturn(Guid SubmissionId);

/// <summary>Handles <see cref="ValidateReturn"/>. Any maker or checker of the bank may validate.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
/// <param name="validator">Runs the validation engine.</param>
public sealed class ValidateReturnHandler(IAppDbContext db, ICurrentActor currentActor, ReturnValidator validator)
    : ICommandHandler<ValidateReturn, Result<ValidationOutcome>>
{
    /// <inheritdoc />
    public async Task<Result<ValidationOutcome>> HandleAsync(ValidateReturn command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var actor = await BankReturnAccess.BankActorAsync(currentActor, cancellationToken);
        if (actor.IsFailure)
        {
            return actor.Error!;
        }

        if (!actor.Value.HasRole(Role.BankMaker) && !actor.Value.HasRole(Role.BankChecker))
        {
            return SubmissionErrors.RoleRequired;
        }

        var submission = await BankReturnAccess.LoadSubmissionAsync(db, actor.Value, command.SubmissionId, cancellationToken);
        if (submission is null)
        {
            return SubmissionErrors.NotFound;
        }

        var template = await BankReturnAccess.LoadTemplateAsync(db, submission.TemplateVersionId, cancellationToken);
        var outcome = await validator.ValidateAsync(submission, template, cancellationToken);
        if (outcome.IsFailure)
        {
            return outcome;
        }

        var saved = await db.SaveOrConflictAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : outcome;
    }
}

/// <summary>Records why a warning is acceptable (at least 20 characters); the checker cannot submit without one.</summary>
/// <param name="SubmissionId">The submission id.</param>
/// <param name="FindingId">The warning finding id.</param>
/// <param name="Justification">The justification.</param>
public sealed record JustifyReturnWarning(Guid SubmissionId, Guid FindingId, string Justification);

/// <summary>Handles <see cref="JustifyReturnWarning"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class JustifyReturnWarningHandler(
    IAppDbContext db, ICurrentActor currentActor, TimeProvider timeProvider, ILogger<JustifyReturnWarningHandler> logger)
    : ICommandHandler<JustifyReturnWarning, Result>
{
    /// <inheritdoc />
    public async Task<Result> HandleAsync(JustifyReturnWarning command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var actor = await BankReturnAccess.BankActorAsync(currentActor, cancellationToken);
        if (actor.IsFailure)
        {
            return actor;
        }

        var submission = await BankReturnAccess.LoadSubmissionAsync(db, actor.Value, command.SubmissionId, cancellationToken);
        if (submission is null)
        {
            return SubmissionErrors.NotFound;
        }

        var justified = submission.JustifyWarning(command.FindingId, command.Justification, actor.Value, timeProvider.GetUtcNow());
        if (justified.IsFailure)
        {
            return justified;
        }

        var saved = await db.SaveOrConflictAsync(cancellationToken);
        if (saved.IsSuccess)
        {
            var rule = submission.CurrentFindings.First(f => f.Id == command.FindingId).RuleCode;
            ReturnsLog.WarningJustified(logger, submission.Id, command.FindingId, rule);
        }

        return saved;
    }
}
