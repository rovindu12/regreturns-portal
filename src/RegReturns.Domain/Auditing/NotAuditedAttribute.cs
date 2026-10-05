namespace RegReturns.Domain.Auditing;

/// <summary>
/// Keeps a property out of the before and after values that data-change audit entries record (ADR 0024). Use it for
/// secrets, personal contact data, large binary content and pure bookkeeping (a value whose change is not itself an
/// event worth auditing). A change to such a property alone writes no audit entry.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class NotAuditedAttribute : Attribute;
