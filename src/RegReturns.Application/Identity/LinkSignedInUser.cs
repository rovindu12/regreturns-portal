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
/// <param name="InstitutionCode">The institution's code as RegReturns stores it (canonical case), for bank users.</param>
public sealed record SignedInUserLink(Guid AppUserId, string DisplayName, string? InstitutionCode = null);

/// <summary>
/// Handles <see cref="LinkSignedInUser"/>. Name, e-mail and roles follow WSO2; the institution and the account status
/// are RegReturns' own (an administrator changes them here first), so a token that disagrees is refused.
/// </summary>
/// <param name="db">The unit of work.</param>
public sealed class LinkSignedInUserHandler(IAppDbContext db) : ICommandHandler<LinkSignedInUser, Result<SignedInUserLink>>
{
    /// <summary>The institution code in the token does not match a known institution.</summary>
    public static readonly Error UnknownInstitution = new(
        "User.UnknownInstitution", "The institution in the sign-in token is not known to RegReturns.");

    /// <summary>The institution in the token has been deactivated.</summary>
    public static readonly Error InactiveInstitution = new(
        "User.InstitutionInactive", "The institution in the sign-in token is no longer active.");

    /// <summary>WSO2 did not send an e-mail address, which the user directory requires.</summary>
    public static readonly Error EmailMissing = new(
        "User.EmailMissing", "The sign-in token has no e-mail address.");

    /// <summary>The user is disabled in RegReturns (WSO2 may not have caught up yet).</summary>
    public static readonly Error UserDisabled = new(
        "User.Disabled", "The user is disabled in RegReturns.");

    /// <summary>The token names a different institution from the one RegReturns holds for the user.</summary>
    public static readonly Error InstitutionChanged = new(
        "User.InstitutionChanged", "The institution in the sign-in token differs from the user's institution in RegReturns.");

    /// <summary>The user name belongs to a RegReturns user already linked to another WSO2 account.</summary>
    public static readonly Error IdentityConflict = new(
        "User.IdentityConflict", "The user name is linked to a different WSO2 account.");

    /// <summary>The user name belongs to the user an API client acts through, which no person may sign in as (ADR 0026).</summary>
    public static readonly Error ApiClientUser = new(
        "User.ApiClientUser", "The user name belongs to an API client, which cannot sign in to the portal.");

    /// <inheritdoc />
    public async Task<Result<SignedInUserLink>> HandleAsync(LinkSignedInUser command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            return EmailMissing;
        }

        Guid? institutionId = null;
        string? institutionCode = null;
        if (!string.IsNullOrWhiteSpace(command.InstitutionCode))
        {
            var institution = await db.Institutions
                .Where(i => i.Code == command.InstitutionCode)
                .Select(i => new { i.Id, i.Code, i.IsActive })
                .SingleOrDefaultAsync(cancellationToken);
            if (institution is null)
            {
                return UnknownInstitution;
            }

            if (!institution.IsActive)
            {
                return InactiveInstitution;
            }

            (institutionId, institutionCode) = (institution.Id, institution.Code);
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
            var refusal = CheckExisting(user, command.SubjectId, institutionId);
            if (refusal is not null)
            {
                return refusal;
            }

            var synced = user.SyncFromDirectory(displayName, command.Email, user.InstitutionId, command.Roles);
            if (synced.IsFailure)
            {
                return synced.Error!;
            }
        }

        if (user.Wso2UserId != command.SubjectId)
        {
            user.LinkIdentity(command.SubjectId);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SignedInUserLink(user.Id, user.DisplayName, institutionCode);
    }

    private static Error? CheckExisting(AppUser user, string subjectId, Guid? institutionId)
    {
        if (user.IsApiClientUser)
        {
            return ApiClientUser;
        }

        // Demo resets re-create WSO2 users with new ids, so demo accounts follow the user name. A real account that is
        // already linked keeps its WSO2 id: a re-used user name must not inherit someone else's history.
        if (user.Wso2UserId is not null && user.Wso2UserId != subjectId && !user.IsDemoAccount)
        {
            return IdentityConflict;
        }

        if (user.Status == UserStatus.Disabled)
        {
            return UserDisabled;
        }

        return user.InstitutionId != institutionId ? InstitutionChanged : null;
    }
}
