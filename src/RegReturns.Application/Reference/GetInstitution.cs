using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Institutions;

namespace RegReturns.Application.Reference;

/// <summary>
/// Reads an institution by code as an API client sees it. A client sees only the institution it acts for, so any
/// other code, whether it exists or not, gets the same empty answer and reveals nothing about other banks (plan §4.4).
/// </summary>
/// <param name="CallerInstitutionId">The id of the institution the calling client acts for.</param>
/// <param name="Code">The requested institution code, matched case-insensitively.</param>
public sealed record GetInstitution(Guid CallerInstitutionId, string Code);

/// <summary>Reference data about an institution.</summary>
/// <param name="Code">The institution code.</param>
/// <param name="Name">The registered name.</param>
/// <param name="LicenceCategory">The licence category.</param>
public sealed record InstitutionReference(string Code, string Name, LicenceCategory LicenceCategory);

/// <summary>
/// Handles <see cref="GetInstitution"/>; returns <see langword="null"/> unless the code is the caller's own active
/// institution.
/// </summary>
/// <param name="db">The unit of work.</param>
public sealed class GetInstitutionHandler(IAppDbContext db) : IQueryHandler<GetInstitution, InstitutionReference?>
{
    /// <inheritdoc />
    public async Task<InstitutionReference?> HandleAsync(GetInstitution query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Codes are stored upper-case (Guard.Code), so normalising here keeps the match independent of collation.
        var code = query.Code.Trim().ToUpperInvariant();
        return await db.Institutions
            .AsNoTracking()
            .Where(i => i.Id == query.CallerInstitutionId && i.Code == code && i.IsActive)
            .Select(i => new InstitutionReference(i.Code, i.Name, i.LicenceCategory))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
