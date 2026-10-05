using System.Security.Claims;

using RegReturns.Application.Identity;
using RegReturns.Domain.Auditing;

namespace RegReturns.Application.Auditing;

/// <summary>Who did something, as recorded in the audit trail.</summary>
/// <param name="Type">The kind of actor.</param>
/// <param name="SubjectId">The WSO2 user id, the client id, or <see cref="AnonymousSubject"/>.</param>
/// <param name="DisplayName">The display name, if known.</param>
/// <param name="InstitutionCode">The actor's institution code, if any.</param>
public sealed record AuditActor(ActorType Type, string SubjectId, string? DisplayName, string? InstitutionCode)
{
    /// <summary>Subject id recorded for callers that are not signed in.</summary>
    public const string AnonymousSubject = "anonymous";

    /// <summary>Gets the actor for callers that are not signed in.</summary>
    public static AuditActor Anonymous { get; } = new(ActorType.Anonymous, AnonymousSubject, null, null);

    /// <summary>
    /// Derives the actor from a WSO2 principal: client-credentials tokens (<c>aut=APPLICATION</c>) are API clients,
    /// any other principal with a subject is a user, and everything else is anonymous.
    /// </summary>
    /// <param name="principal">The current principal, if any.</param>
    /// <returns>The actor.</returns>
    public static AuditActor FromPrincipal(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return Anonymous;
        }

        var institution = NullIfBlank(principal.FindFirst(ClaimNames.InstitutionId)?.Value);
        if (principal.HasClaim(ClaimNames.AuthorizedUserType, ClaimNames.ApplicationTokenType))
        {
            var clientId = NullIfBlank(principal.FindFirst(ClaimNames.ClientId)?.Value)
                ?? NullIfBlank(principal.FindFirst(ClaimNames.AuthorizedParty)?.Value)
                ?? NullIfBlank(principal.FindFirst(ClaimNames.Subject)?.Value);
            return clientId is null ? Anonymous : new AuditActor(ActorType.ApiClient, clientId, null, institution);
        }

        var subject = NullIfBlank(principal.FindFirst(ClaimNames.Subject)?.Value);
        if (subject is null)
        {
            return Anonymous;
        }

        var displayName = NullIfBlank(principal.FindFirst(ClaimNames.Name)?.Value)
            ?? NullIfBlank(principal.FindFirst(ClaimNames.UserName)?.Value);
        return new AuditActor(ActorType.User, subject, displayName, institution);
    }

    /// <summary>Builds the record to append for an action by this actor.</summary>
    /// <param name="action">What happened.</param>
    /// <param name="details">Extra non-sensitive context.</param>
    /// <param name="ipAddress">The caller's IP address.</param>
    /// <param name="correlationId">The W3C trace id of the request.</param>
    /// <param name="entityType">The affected entity type, if any.</param>
    /// <param name="entityId">The affected entity id, if any.</param>
    /// <returns>The audit record.</returns>
    public AuditRecord ToRecord(
        AuditAction action,
        string? details = null,
        string? ipAddress = null,
        string? correlationId = null,
        string? entityType = null,
        string? entityId = null) =>
        new(action, Type, SubjectId, DisplayName, InstitutionCode, entityType, entityId, details, ipAddress, correlationId);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
