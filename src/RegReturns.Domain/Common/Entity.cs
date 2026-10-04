namespace RegReturns.Domain.Common;

/// <summary>
/// Base type for entities identified by a <see cref="Guid"/>.
/// Identifiers are time-ordered version 7 GUIDs (RFC 9562) assigned when the entity is created,
/// so aggregates can reference each other before anything is saved.
/// </summary>
public abstract class Entity
{
    /// <summary>Initializes a new instance of the <see cref="Entity"/> class with a new identifier.</summary>
    protected Entity() => Id = Guid.CreateVersion7();

    /// <summary>Gets the unique identifier of the entity.</summary>
    public Guid Id { get; private set; }
}
