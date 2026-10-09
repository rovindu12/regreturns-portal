namespace RegReturns.Infrastructure.Persistence;

/// <summary>Database roles the schema defines (ADR 0034).</summary>
public static class DatabaseRoles
{
    /// <summary>
    /// What the running portal and API may do: read and write data and run procedures, but never change the schema,
    /// switch off a trigger, or change or delete an audit entry. The production deployment maps its application login
    /// to this role; the migrator and the backups use the administrator login.
    /// </summary>
    public const string Runtime = "regreturns_runtime";
}
