using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;

namespace RegReturns.Application.Identity;

/// <summary>Resolves the signed-in caller to the domain <see cref="Actor"/> that use cases pass to aggregates.</summary>
public interface ICurrentActor
{
    /// <summary>
    /// Returns the caller as an actor, built from their RegReturns user record: roles and institution as RegReturns
    /// holds them (synced from WSO2 at sign-in), never from anything the request itself claims.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The actor, or <see cref="ActorErrors.NotLinked"/> when the caller has no active user record.</returns>
    Task<Result<Actor>> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Errors raised while resolving the caller.</summary>
public static class ActorErrors
{
    /// <summary>The caller is not signed in, or their WSO2 account is not linked to an active RegReturns user.</summary>
    public static readonly Error NotLinked = new(
        "User.NotLinked", "Your sign-in is not linked to an active RegReturns user. Sign out and sign in again.");
}

/// <summary>Loads the caller's <see cref="AppUser"/> once per request scope.</summary>
/// <param name="currentUser">The signed-in caller.</param>
/// <param name="db">The unit of work.</param>
public sealed class CurrentActor(ICurrentUser currentUser, IAppDbContext db) : ICurrentActor
{
    private Result<Actor>? _actor;

    /// <inheritdoc />
    public async Task<Result<Actor>> GetAsync(CancellationToken cancellationToken)
    {
        if (_actor is not null)
        {
            return _actor;
        }

        var subject = currentUser.SubjectId;
        var user = string.IsNullOrEmpty(subject)
            ? null
            : await db.Users.AsNoTracking()
                .SingleOrDefaultAsync(u => u.Wso2UserId == subject && u.Status == UserStatus.Active, cancellationToken);
        _actor = user is null ? ActorErrors.NotLinked : user.ToActor();
        return _actor;
    }
}
