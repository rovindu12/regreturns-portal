using System.Diagnostics;

using RegReturns.Application.Auditing;
using RegReturns.Domain.Auditing;

namespace RegReturns.Migrator.Legacy;

/// <summary>Names the migrator as the actor of every audited save it makes, with the run's trace id as correlation id.</summary>
internal sealed class MigratorAuditContext : IAuditContext
{
    /// <summary>The audit subject of the migrator.</summary>
    public const string Subject = "regreturns-migrator";

    private static readonly AuditActor Actor = new(ActorType.System, Subject, "Legacy data migration", null);

    /// <inheritdoc />
    public AuditOrigin Current => new(Actor, null, Activity.Current?.TraceId.ToHexString());
}
