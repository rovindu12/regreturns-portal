using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;

namespace RegReturns.Application.Identity;

/// <summary>The API client behind a request, as the API host sees it (the token's <c>azp</c> or <c>client_id</c>).</summary>
public interface ICurrentClient
{
    /// <summary>Gets the OAuth client id, or <see langword="null"/> when the request has no client token.</summary>
    string? ClientId { get; }
}

/// <summary>
/// The API's <see cref="ICurrentActor"/> (ADR 0026): resolves the token's client id to its active registration in
/// <c>iam.ApiClients</c> and then to the client user it acts through, so API requests run the portal's use cases and
/// rules as that user. A client that is unknown, inactive, of an inactive bank or without a client user is
/// <see cref="ActorErrors.NotLinked"/>.
/// </summary>
/// <param name="currentClient">The calling client.</param>
/// <param name="db">The unit of work.</param>
public sealed class ClientActor(ICurrentClient currentClient, IAppDbContext db) : ICurrentActor
{
    private Result<Actor>? _actor;

    /// <inheritdoc />
    public async Task<Result<Actor>> GetAsync(CancellationToken cancellationToken)
    {
        if (_actor is not null)
        {
            return _actor;
        }

        var clientId = currentClient.ClientId;
        var match = string.IsNullOrEmpty(clientId)
            ? null
            : await (
                from client in db.ApiClients
                join institution in db.Institutions on client.InstitutionId equals institution.Id
                join user in db.Users on client.Id equals user.ApiClientId
                where client.Wso2ClientId == clientId && client.IsActive && institution.IsActive && user.Status == UserStatus.Active
                select new { client.Wso2ClientId, User = user })
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);

        // The column uses the database's case-insensitive collation; client ids are case-sensitive.
        _actor = match is not null && string.Equals(match.Wso2ClientId, clientId, StringComparison.Ordinal)
            ? match.User.ToActor()
            : ActorErrors.NotLinked;
        return _actor;
    }
}
