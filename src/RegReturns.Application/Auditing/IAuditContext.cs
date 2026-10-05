using RegReturns.Domain.Auditing;

namespace RegReturns.Application.Auditing;

/// <summary>
/// Who is acting in the current unit of work and where the request came from, for the audit entries written with a
/// data change (ADR 0024). Each host implements it from its request; with no request the actor is
/// <see cref="AuditActor.System"/>.
/// </summary>
public interface IAuditContext
{
    /// <summary>Gets the actor, IP address and correlation id to record now.</summary>
    AuditOrigin Current { get; }
}

/// <summary>The actor and request behind an audited action.</summary>
/// <param name="Actor">Who acted.</param>
/// <param name="IpAddress">The caller's IP address, if there is a request.</param>
/// <param name="CorrelationId">The W3C trace id of the request, if there is one.</param>
public sealed record AuditOrigin(AuditActor Actor, string? IpAddress, string? CorrelationId)
{
    /// <summary>Gets the origin of work the application does on its own.</summary>
    public static AuditOrigin System { get; } = new(AuditActor.System, null, null);

    /// <summary>Builds the record to append for an action from this origin.</summary>
    /// <param name="action">What happened.</param>
    /// <param name="details">Extra non-sensitive context.</param>
    /// <returns>The audit record.</returns>
    public AuditRecord ToRecord(AuditAction action, string? details = null) =>
        Actor.ToRecord(action, details, IpAddress, CorrelationId);
}
