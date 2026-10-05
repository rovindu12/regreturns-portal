namespace RegReturns.Domain.Auditing;

/// <summary>
/// Keeps a property out of the before and after values that data-change audit entries record (ADR 0024). Use it for
/// secrets, personal contact data, large binary content and pure bookkeeping (a value whose change is not itself an
/// event worth auditing). A change to such a property alone writes no audit entry. On an entity type it keeps the
/// whole type out of the trail: only for technical records whose purpose is audited elsewhere, such as the stored
/// responses of idempotent API requests (ADR 0027).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class NotAuditedAttribute : Attribute;
