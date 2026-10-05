using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Identity;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Returns;

/// <summary>
/// Loads what bank use cases work on, always scoped to the caller's own institution: another bank's return is reported
/// as not found, never as forbidden, so its existence does not leak.
/// </summary>
internal static class BankReturnAccess
{
    /// <summary>Returns the caller if they belong to a bank.</summary>
    public static async Task<Result<Actor>> BankActorAsync(ICurrentActor currentActor, CancellationToken cancellationToken)
    {
        var actor = await currentActor.GetAsync(cancellationToken);
        if (actor.IsFailure)
        {
            return actor;
        }

        return actor.Value.InstitutionId is null ? SubmissionErrors.WrongInstitution : actor;
    }

    /// <summary>Loads a submission of the caller's bank with its values and findings, tracked for changes.</summary>
    public static Task<Submission?> LoadSubmissionAsync(
        IAppDbContext db, Actor actor, Guid submissionId, CancellationToken cancellationToken) =>
        db.Submissions
            .Include(s => s.Values)
            .Include(s => s.Findings)
            .SingleOrDefaultAsync(s => s.Id == submissionId && s.InstitutionId == actor.InstitutionId, cancellationToken);

    /// <summary>Loads a template version with its fields and rules, read-only.</summary>
    public static Task<TemplateVersion> LoadTemplateAsync(IAppDbContext db, Guid templateVersionId, CancellationToken cancellationToken) =>
        db.TemplateVersions.AsNoTracking()
            .Include(v => v.Fields)
            .Include(v => v.Rules)
            .SingleAsync(v => v.Id == templateVersionId, cancellationToken);

    /// <summary>Returns the obligation's live submission (the newest one not rejected), if any.</summary>
    public static Task<Guid?> LiveSubmissionIdAsync(IAppDbContext db, Guid obligationId, CancellationToken cancellationToken) =>
        db.Submissions
            .Where(s => s.ObligationId == obligationId && s.Status != SubmissionStatus.Rejected)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
