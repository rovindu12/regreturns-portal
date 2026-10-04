using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;

namespace RegReturns.Application.Identity;

/// <summary>
/// Links a person who has just signed in through WSO2 to their <see cref="AppUser"/> projection,
/// creating or updating it from the token claims (WSO2 is the source of truth).
/// </summary>
/// <param name="SubjectId">The WSO2 user id (<c>sub</c>).</param>
/// <param name="UserName">The WSO2 user name.</param>
/// <param name="DisplayName">The display name, if WSO2 sent one.</param>
/// <param name="Email">The e-mail address, if WSO2 sent one.</param>
/// <param name="InstitutionCode">The <c>institution_id</c> claim, for bank users.</param>
/// <param name="Roles">The known roles from the token.</param>
public sealed record LinkSignedInUser(
    string SubjectId,
    string UserName,
    string? DisplayName,
    string? Email,
    string? InstitutionCode,
    IReadOnlyCollection<Role> Roles);

/// <summary>The projection a signed-in user was linked to.</summary>
/// <param name="AppUserId">The internal user id.</param>
/// <param name="DisplayName">The display name to show and audit.</param>
public sealed record SignedInUserLink(Guid AppUserId, string DisplayName);

/// <summary>Handles <see cref="LinkSignedInUser"/>.</summary>
/// <param name="db">The unit of work.</param>
public sealed class LinkSignedInUserHandler(IAppDbContext db) : ICommandHandler<LinkSignedInUser, Result<SignedInUserLink>>
{
    /// <summary>The institution code in the token does not match a known institution.</summary>
    public static readonly Error UnknownInstitution = new(
        "User.UnknownInstitution", "The institution in the sign-in token is not known to RegReturns.");

    /// <summary>WSO2 did not send an e-mail address, which the user directory requires.</summary>
    public static readonly Error EmailMissing = new(
        "User.EmailMissing", "The sign-in token has no e-mail address.");

    /// <inheritdoc />
    public async Task<Result<SignedInUserLink>> HandleAsync(LinkSignedInUser command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            return EmailMissing;
        }

        Guid? institutionId = null;
        if (!string.IsNullOrWhiteSpace(command.InstitutionCode))
        {
            institutionId = await db.Institutions
                .Where(i => i.Code == command.InstitutionCode)
                .Select(i => (Guid?)i.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (institutionId is null)
            {
                return UnknownInstitution;
            }
        }

        var userName = command.UserName.Trim().ToLowerInvariant();
        var displayName = string.IsNullOrWhiteSpace(command.DisplayName) ? userName : command.DisplayName;
        var user = await db.Users.SingleOrDefaultAsync(u => u.Wso2UserId == command.SubjectId, cancellationToken)
            ?? await db.Users.SingleOrDefaultAsync(u => u.UserName == userName, cancellationToken);

        if (user is null)
        {
            var created = AppUser.Create(userName, displayName, command.Email, institutionId, command.Roles);
            if (created.IsFailure)
            {
                return created.Error!;
            }

            user = created.Value;
            await db.Users.AddAsync(user, cancellationToken);
        }
        else
        {
            var synced = user.SyncFromDirectory(displayName, command.Email, institutionId, command.Roles);
            if (synced.IsFailure)
            {
                return synced.Error!;
            }
        }

        // Demo resets re-create WSO2 users with new ids, so the link follows the user name.
        if (user.Wso2UserId != command.SubjectId)
        {
            user.LinkIdentity(command.SubjectId);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SignedInUserLink(user.Id, user.DisplayName);
    }
}
