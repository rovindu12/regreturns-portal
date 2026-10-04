namespace RegReturns.Domain.Identity;

/// <summary>
/// The authenticated person performing an action, as seen by the domain.
/// Built by the application layer from the current user's claims and directory record.
/// </summary>
public sealed class Actor
{
    /// <summary>Initializes a new instance of the <see cref="Actor"/> class.</summary>
    /// <param name="userId">The internal user id (the <see cref="AppUser"/> id).</param>
    /// <param name="displayName">The display name recorded in workflow history.</param>
    /// <param name="roles">The roles the actor holds.</param>
    /// <param name="institutionId">The actor's bank, or <see langword="null"/> for regulator staff.</param>
    public Actor(Guid userId, string displayName, IEnumerable<Role> roles, Guid? institutionId)
    {
        ArgumentNullException.ThrowIfNull(roles);
        UserId = userId;
        DisplayName = displayName;
        Roles = roles.ToHashSet();
        InstitutionId = institutionId;
    }

    /// <summary>Gets the internal user id.</summary>
    public Guid UserId { get; }

    /// <summary>Gets the display name.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the roles the actor holds.</summary>
    public IReadOnlySet<Role> Roles { get; }

    /// <summary>Gets the actor's bank, or <see langword="null"/> for regulator staff.</summary>
    public Guid? InstitutionId { get; }

    /// <summary>Gets a value indicating whether the actor works for the regulator rather than a bank.</summary>
    public bool IsRegulatorStaff => InstitutionId is null;

    /// <summary>Returns whether the actor holds the given role.</summary>
    /// <param name="role">The role to check.</param>
    /// <returns><see langword="true"/> if the actor holds the role.</returns>
    public bool HasRole(Role role) => Roles.Contains(role);

    /// <summary>Returns whether the actor is a member of the given bank.</summary>
    /// <param name="institutionId">The bank id.</param>
    /// <returns><see langword="true"/> if the actor belongs to the bank.</returns>
    public bool BelongsTo(Guid institutionId) => InstitutionId == institutionId;
}
