using System.Diagnostics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RegReturns.Application.Auditing;
using RegReturns.Application.Diagnostics;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;

namespace RegReturns.Application.Demo;

/// <summary>Asks for the demo data to be reset (ADR 0031).</summary>
/// <param name="Trigger">What started the reset.</param>
public sealed record ResetDemo(DemoResetTrigger Trigger);

/// <summary>
/// Resets the demo: only in demo mode, only for a system administrator (with the cooldown) or the scheduled job acting
/// as the system (without it). <see cref="IDemoReset"/> does the work and records the audit event in the same
/// transaction.
/// </summary>
/// <param name="reset">Runs the reset.</param>
/// <param name="currentActor">Resolves the administrator pressing the button.</param>
/// <param name="auditContext">Who is acting, for the audit event; the system outside a request.</param>
/// <param name="options">The demo settings.</param>
/// <param name="logger">The logger.</param>
public sealed class ResetDemoHandler(
    IDemoReset reset,
    ICurrentActor currentActor,
    IAuditContext auditContext,
    IOptions<DemoOptions> options,
    ILogger<ResetDemoHandler> logger) : ICommandHandler<ResetDemo, Result<DemoResetReport>>
{
    /// <inheritdoc />
    public async Task<Result<DemoResetReport>> HandleAsync(ResetDemo command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var activity = RegReturnsTelemetry.ActivitySource.StartActivity("ResetDemo");
        activity?.SetTag("regreturns.demo.trigger", command.Trigger.ToString());

        if (!options.Value.Enabled)
        {
            return Refuse(command.Trigger, DemoErrors.Disabled, activity);
        }

        var origin = auditContext.Current;
        TimeSpan? cooldown = null;
        if (command.Trigger == DemoResetTrigger.Manual)
        {
            var actor = await currentActor.GetAsync(cancellationToken);
            if (actor.IsFailure || !actor.Value.HasRole(Role.SystemAdmin))
            {
                return Refuse(command.Trigger, DemoErrors.AdminsOnly, activity);
            }

            cooldown = TimeSpan.FromMinutes(options.Value.ResetCooldownMinutes);
        }
        else if (origin.Actor.Type != ActorType.System)
        {
            // Only work with no request behind it acts as the system: a caller cannot skip the cooldown.
            return Refuse(command.Trigger, DemoErrors.AdminsOnly, activity);
        }

        var result = await reset.ResetAsync(new DemoResetRequest(command.Trigger, origin, cooldown), cancellationToken);
        if (result.IsFailure)
        {
            return Refuse(command.Trigger, result.Error!, activity);
        }

        var report = result.Value;
        DemoLog.Reset(logger, command.Trigger, report.RowsRemoved, report.Seeded.Obligations, report.Seeded.Submissions, report.ElapsedMs, report.AuditSequence);
        activity?.SetTag(RegReturnsTelemetry.OutcomeTag, "done");
        var tags = new TagList { { "trigger", command.Trigger.ToString() }, { RegReturnsTelemetry.OutcomeTag, "done" } };
        RegReturnsTelemetry.DemoResets.Add(1, tags);
        RegReturnsTelemetry.DemoResetDuration.Record(report.ElapsedMs, tags);
        return report;
    }

    private Error Refuse(DemoResetTrigger trigger, Error error, Activity? activity)
    {
        DemoLog.Refused(logger, trigger, error.Code);
        activity?.SetTag(RegReturnsTelemetry.OutcomeTag, "refused");
        activity?.SetTag(RegReturnsTelemetry.ErrorCodeTag, error.Code);
        RegReturnsTelemetry.DemoResets.Add(
            1,
            new KeyValuePair<string, object?>("trigger", trigger.ToString()),
            new KeyValuePair<string, object?>(RegReturnsTelemetry.OutcomeTag, "refused"),
            new KeyValuePair<string, object?>(RegReturnsTelemetry.ErrorCodeTag, error.Code));
        return error;
    }
}
