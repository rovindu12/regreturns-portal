using System.Security.Claims;

using RegReturns.Application.Auditing;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;
using RegReturns.Infrastructure.Auditing;

namespace RegReturns.Web.Identity;

/// <summary>
/// Runs when WSO2 has returned a valid id_token (OIDC <c>OnTokenValidated</c>): normalises the claims, links the user
/// to the <c>AppUser</c> projection (JIT) and records <see cref="AuditAction.SignIn"/>. A user the portal cannot link
/// is refused with a stable error code and an <see cref="AuditAction.AuthenticationFailed"/> entry.
/// </summary>
/// <param name="linkHandler">Links the signed-in user to their projection.</param>
/// <param name="auditTrail">Records the sign-in.</param>
/// <param name="failureAuditor">Records refused sign-ins, de-duplicated per minute.</param>
/// <param name="auditContext">Attributes the projection's audited changes to the user signing in.</param>
/// <param name="logger">The logger.</param>
public sealed partial class SignInProcessor(
    ICommandHandler<LinkSignedInUser, Result<SignedInUserLink>> linkHandler,
    IAuditTrail auditTrail,
    AccessDeniedAuditor failureAuditor,
    PortalAuditContext auditContext,
    ILogger<SignInProcessor> logger)
{
    /// <summary>Processes a validated sign-in.</summary>
    /// <param name="tokenPrincipal">The principal built from the id_token.</param>
    /// <param name="origin">Where the sign-in callback came from.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The normalised principal to keep in the session cookie, or the reason the sign-in was refused.</returns>
    public async Task<Result<ClaimsPrincipal>> ProcessAsync(
        ClaimsPrincipal tokenPrincipal, RequestOrigin origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokenPrincipal);
        ArgumentNullException.ThrowIfNull(origin);

        var claims = PortalClaims.Normalize(tokenPrincipal);
        if (claims.UnknownRoles.Count > 0)
        {
            LogUnknownRolesDropped(logger, claims.UnknownRoles.Count, string.Join(", ", claims.UnknownRoles));
        }

        Result<SignedInUserLink> link;
        using (auditContext.ActAs(claims.Principal))
        {
            // The request has no signed-in user yet; changes to the user's record are theirs, not anonymous.
            link = await LinkAsync(claims, cancellationToken);
        }

        var error = link.Error;
        if (error is not null)
        {
            LogSignInRejected(logger, error.Code);
            await failureAuditor.RecordAsync(
                claims.Principal, AuditAction.AuthenticationFailed, origin.Path, error.Code, origin.IpAddress, origin.TraceId, cancellationToken);
            return error;
        }

        UseCanonicalInstitutionCode(claims, link.Value.InstitutionCode);
        var details = claims.AuthenticationMethods.Count == 0 ? null : $"amr={string.Join(',', claims.AuthenticationMethods)}";
        await auditTrail.RecordAsync(
            AuditActor.FromPrincipal(claims.Principal).ToRecord(AuditAction.SignIn, details, origin.IpAddress, origin.TraceId),
            cancellationToken);
        LogSignedIn(logger, claims.Subject!, claims.InstitutionCode, claims.Roles.Count);
        return claims.Principal;
    }

    private async Task<Result<SignedInUserLink>> LinkAsync(NormalizedClaims claims, CancellationToken cancellationToken)
    {
        if (claims.Subject is null)
        {
            return SignInErrors.SubjectMissing;
        }

        if (claims.UserName is null)
        {
            return SignInErrors.UserNameMissing;
        }

        // Without a session id, back-channel logout could never end this session.
        if (claims.SessionId is null)
        {
            return SignInErrors.SessionMissing;
        }

        return await linkHandler.HandleAsync(
            new LinkSignedInUser(claims.Subject, claims.UserName, claims.Name, claims.Email, claims.InstitutionCode, claims.Roles),
            cancellationToken);
    }

    // The token's institution matched case-insensitively; the cookie carries the code exactly as RegReturns stores it.
    private static void UseCanonicalInstitutionCode(NormalizedClaims claims, string? institutionCode)
    {
        if (institutionCode is null || claims.Principal.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        var current = identity.FindFirst(ClaimNames.InstitutionId);
        if (current is not null && !string.Equals(current.Value, institutionCode, StringComparison.Ordinal))
        {
            identity.RemoveClaim(current);
            identity.AddClaim(new Claim(ClaimNames.InstitutionId, institutionCode));
        }
    }

    [LoggerMessage(EventId = 3101, Level = LogLevel.Warning, Message = "Dropped {Count} unknown role(s) from the sign-in token: {Roles}")]
    private static partial void LogUnknownRolesDropped(ILogger logger, int count, string roles);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Information, Message = "User {SubjectId} ({InstitutionCode}) signed in with {RoleCount} role(s)")]
    private static partial void LogSignedIn(ILogger logger, string subjectId, string? institutionCode, int roleCount);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Warning, Message = "Sign-in refused: {ErrorCode}")]
    private static partial void LogSignInRejected(ILogger logger, string errorCode);
}
