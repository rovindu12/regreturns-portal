using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Messaging;

namespace RegReturns.Application.Identity;

/// <summary>Finds the institution a client-credentials token may act for.</summary>
/// <param name="ClientId">The token's <c>azp</c> or <c>client_id</c>.</param>
public sealed record GetApiClientInstitution(string ClientId);

/// <summary>The institution behind an API client.</summary>
/// <param name="InstitutionId">The institution id.</param>
/// <param name="InstitutionCode">The institution code.</param>
public sealed record ApiClientInstitution(Guid InstitutionId, string InstitutionCode);

/// <summary>Handles <see cref="GetApiClientInstitution"/>; returns <see langword="null"/> for unknown or inactive clients.</summary>
/// <param name="db">The unit of work.</param>
public sealed class GetApiClientInstitutionHandler(IAppDbContext db) : IQueryHandler<GetApiClientInstitution, ApiClientInstitution?>
{
    /// <inheritdoc />
    public async Task<ApiClientInstitution?> HandleAsync(GetApiClientInstitution query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var match = await (
            from client in db.ApiClients
            join institution in db.Institutions on client.InstitutionId equals institution.Id
            where client.Wso2ClientId == query.ClientId && client.IsActive && institution.IsActive
            select new { client.Wso2ClientId, institution.Id, institution.Code })
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        // The column uses the database's case-insensitive collation; client ids are case-sensitive.
        return match is not null && string.Equals(match.Wso2ClientId, query.ClientId, StringComparison.Ordinal)
            ? new ApiClientInstitution(match.Id, match.Code)
            : null;
    }
}
