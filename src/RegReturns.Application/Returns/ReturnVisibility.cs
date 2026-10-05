using RegReturns.Domain.Identity;
using RegReturns.Domain.Submissions;

namespace RegReturns.Application.Returns;

/// <summary>
/// Which returns a caller may see. Bank staff see their own bank's returns, drafts included. Regulator staff see every
/// bank's returns once they have been submitted: a draft that was never submitted is the bank's own work in progress.
/// Anything else is reported as not found, so another bank's ids reveal nothing.
/// </summary>
internal static class ReturnVisibility
{
    /// <summary>Filters submissions to the ones the actor may see.</summary>
    /// <param name="submissions">The submissions.</param>
    /// <param name="actor">The caller.</param>
    /// <returns>The visible submissions.</returns>
    public static IQueryable<Submission> VisibleTo(this IQueryable<Submission> submissions, Actor actor) =>
        actor.InstitutionId is { } institutionId
            ? submissions.Where(s => s.InstitutionId == institutionId)
            : submissions.Where(s => s.FirstSubmittedAt != null);
}
