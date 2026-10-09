using System.Diagnostics;

using RegReturns.Application.Auditing;
using RegReturns.Domain.Auditing;

namespace RegReturns.Migrator.Auditing;

/// <summary>
/// Names the migrator as the actor of every audit entry it writes, with the task it was run for as the actor's name and
/// the run's trace id as correlation id.
/// </summary>
/// <param name="task">What the migrator was run for, such as "Legacy data migration".</param>
internal sealed class MigratorAuditContext(string task) : IAuditContext
{
    /// <summary>The audit subject of the migrator.</summary>
    public const string Subject = "regreturns-migrator";

    private readonly AuditActor _actor = new(ActorType.System, Subject, task, null);

    /// <summary>Gets the context of the <c>legacy</c> command (ADR 0029).</summary>
    public static MigratorAuditContext LegacyMigration { get; } = new("Legacy data migration");

    /// <summary>Gets the context of the <c>verify-audit</c> command (ADR 0034).</summary>
    public static MigratorAuditContext ChainVerification { get; } = new("Audit chain verification");

    /// <inheritdoc />
    public AuditOrigin Current => new(_actor, null, Activity.Current?.TraceId.ToHexString());
}
