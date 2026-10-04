using System.Globalization;
using System.Security.Claims;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Auditing;
using RegReturns.Domain.Auditing;

namespace RegReturns.Infrastructure.Auditing;

/// <summary>
/// Records denied and failed access in the audit trail, at most once per actor, action and path per minute
/// (once per IP address and action for anonymous callers), so a client retrying in a loop cannot flood the chain
/// (plan §4.6). Shared by the portal and the API.
/// </summary>
/// <param name="auditTrail">The audit trail.</param>
/// <param name="cache">Remembers what was recorded in the current minute.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed partial class AccessDeniedAuditor(
    IAuditTrail auditTrail,
    IMemoryCache cache,
    TimeProvider timeProvider,
    ILogger<AccessDeniedAuditor> logger)
{
    /// <summary>
    /// Records an <see cref="AuditAction.AccessDenied"/> or <see cref="AuditAction.AuthenticationFailed"/> event
    /// unless the same one was recorded in the current minute. Never throws for audit storage failures: the caller
    /// is already refusing the request, and a failed write is logged instead of turning a 403 into a 500.
    /// </summary>
    /// <param name="principal">The caller, if authenticated.</param>
    /// <param name="action">The action to record.</param>
    /// <param name="path">The request path, without the query string.</param>
    /// <param name="reason">The policy or failure reason. Never a token or exception message.</param>
    /// <param name="ipAddress">The caller's IP address.</param>
    /// <param name="correlationId">The W3C trace id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> if an entry was written.</returns>
    public async Task<bool> RecordAsync(
        ClaimsPrincipal? principal,
        AuditAction action,
        string path,
        string? reason,
        string? ipAddress,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        if (action is not (AuditAction.AccessDenied or AuditAction.AuthenticationFailed))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "Only access-denied and authentication-failed events are recorded here.");
        }

        var actor = AuditActor.FromPrincipal(principal);

        // Anonymous callers are keyed by IP so one noisy client cannot hide others, and not by path, because an
        // unauthenticated client can invent paths and would otherwise write one entry per request.
        var anonymous = actor.Type == ActorType.Anonymous;
        var who = anonymous ? $"ip:{ipAddress}" : actor.SubjectId;
        var where = anonymous ? "*" : path;
        var minute = timeProvider.GetUtcNow().ToUnixTimeSeconds() / 60;
        var key = string.Create(CultureInfo.InvariantCulture, $"audit-denied|{action}|{who}|{where}|{minute}");
        if (cache.TryGetValue(key, out _))
        {
            return false;
        }

        cache.Set(key, true, TimeSpan.FromMinutes(2));
        LogAccessDenied(logger, action, actor.Type, path, reason);

        var details = string.IsNullOrWhiteSpace(reason) ? $"path={path}" : $"path={path}; reason={reason}";
        try
        {
            await auditTrail.RecordAsync(actor.ToRecord(action, details, ipAddress, correlationId), cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAuditWriteFailed(logger, ex, action, path);
            return false;
        }
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "{Action} for {ActorType} on {Path} ({Reason})")]
    private static partial void LogAccessDenied(ILogger logger, AuditAction action, ActorType actorType, string path, string? reason);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Error, Message = "Could not write the {Action} audit entry for {Path}")]
    private static partial void LogAuditWriteFailed(ILogger logger, Exception exception, AuditAction action, string path);
}
