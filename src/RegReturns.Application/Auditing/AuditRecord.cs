using RegReturns.Domain.Auditing;

namespace RegReturns.Application.Auditing;

/// <summary>An audit event to append. Never include secrets, tokens, e-mail addresses or return figures.</summary>
/// <param name="Action">What happened.</param>
/// <param name="ActorType">The kind of actor.</param>
/// <param name="ActorSubjectId">The WSO2 subject id, client id or <c>system</c>.</param>
/// <param name="ActorDisplayName">The actor's display name, if known.</param>
/// <param name="InstitutionCode">The actor's institution code, if any.</param>
/// <param name="EntityType">The affected entity type, if any.</param>
/// <param name="EntityId">The affected entity id, if any.</param>
/// <param name="Details">Extra context such as a request path and policy.</param>
/// <param name="IpAddress">The caller's IP address.</param>
/// <param name="CorrelationId">The W3C trace id of the request.</param>
public sealed record AuditRecord(
    AuditAction Action,
    ActorType ActorType,
    string ActorSubjectId,
    string? ActorDisplayName = null,
    string? InstitutionCode = null,
    string? EntityType = null,
    string? EntityId = null,
    string? Details = null,
    string? IpAddress = null,
    string? CorrelationId = null);
