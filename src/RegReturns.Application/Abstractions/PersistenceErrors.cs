using Microsoft.EntityFrameworkCore;

using RegReturns.Domain.Common;

namespace RegReturns.Application.Abstractions;

/// <summary>Errors and helpers for saving changes that another user may have changed at the same time.</summary>
public static class PersistenceErrors
{
    /// <summary>The row changed since it was read.</summary>
    public static readonly Error Conflict = new(
        "Common.Conflict", "Someone else changed this at the same time. Reload the page and try again.");

    /// <summary>
    /// Saves all changes, turning an optimistic-concurrency clash (a <c>rowversion</c> mismatch) into
    /// <see cref="Conflict"/> instead of an exception.
    /// </summary>
    /// <param name="db">The unit of work.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or <see cref="Conflict"/>.</returns>
    public static async Task<Result> SaveOrConflictAsync(this IAppDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict;
        }
    }
}
