using System.Security.Claims;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

using RegReturns.Application.Identity;
using RegReturns.Domain.Auditing;
using RegReturns.Infrastructure.Auditing;
using RegReturns.ServiceDefaults.Web;

namespace RegReturns.Api.Authentication;

/// <summary>
/// Bearer events for the API: refuses tokens without a client id and records failed and denied requests in the
/// audit trail (plan §4.6) through the de-duplicating <see cref="AccessDeniedAuditor"/>. Reasons are exception type
/// or policy names only; token contents and exception messages never reach the trail or the logs.
/// </summary>
/// <param name="auditor">Records denied and failed access.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ApiJwtBearerEvents(AccessDeniedAuditor auditor, ILogger<ApiJwtBearerEvents> logger)
    : JwtBearerEvents
{
    /// <summary>Audit reason for a token with neither <c>client_id</c> nor <c>azp</c>.</summary>
    public const string MissingClientIdReason = "MissingClientId";

    /// <summary>Audit reason for a token whose <c>client_id</c> and <c>azp</c> name different clients.</summary>
    public const string ConflictingClientIdReason = "ConflictingClientId";

    /// <inheritdoc />
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var reason = ClientIdProblem(context.Principal);
        if (reason is null)
        {
            return;
        }

        LogTokenRejected(logger, reason);
        await RecordAsync(context.HttpContext, context.Principal, AuditAction.AuthenticationFailed, reason);
        context.Fail(new SecurityTokenValidationException("The access token does not identify exactly one client."));
    }

    /// <inheritdoc />
    public override Task AuthenticationFailed(AuthenticationFailedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return RecordAsync(context.HttpContext, principal: null, AuditAction.AuthenticationFailed, DescribeFailure(context.Exception));
    }

    /// <inheritdoc />
    public override Task Forbidden(ForbiddenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var http = context.HttpContext;
        return RecordAsync(http, http.User, AuditAction.AccessDenied, PolicyNames(http));
    }

    /// <summary>Names the failure by exception type only, so no token content or message reaches the trail.</summary>
    /// <param name="exception">The validation failure.</param>
    /// <returns>The exception type name, or the distinct inner type names of an <see cref="AggregateException"/>.</returns>
    internal static string DescribeFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is AggregateException aggregate
            ? string.Join(',', aggregate.InnerExceptions.Select(e => e.GetType().Name).Distinct(StringComparer.Ordinal))
            : exception.GetType().Name;
    }

    private static string? ClientIdProblem(ClaimsPrincipal? principal)
    {
        var authorizedParty = NullIfBlank(principal?.FindFirst(ClaimNames.AuthorizedParty)?.Value);
        var clientId = NullIfBlank(principal?.FindFirst(ClaimNames.ClientId)?.Value);
        if (authorizedParty is null && clientId is null)
        {
            return MissingClientIdReason;
        }

        return authorizedParty is not null && clientId is not null && !string.Equals(authorizedParty, clientId, StringComparison.Ordinal)
            ? ConflictingClientIdReason
            : null;
    }

    private static string? PolicyNames(HttpContext http)
    {
        var policies = http.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(data => data.Policy)
            .OfType<string>()
            .Where(policy => policy.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return policies is { Count: > 0 } ? string.Join(',', policies) : null;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private Task<bool> RecordAsync(HttpContext http, ClaimsPrincipal? principal, AuditAction action, string? reason) =>
        auditor.RecordAsync(
            principal,
            action,
            http.Request.PathBase.Add(http.Request.Path).Value ?? "/",
            (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText,
            reason,
            http.Connection.RemoteIpAddress?.ToString(),
            WebDefaultsExtensions.CurrentTraceId(http),
            http.RequestAborted);

    [LoggerMessage(EventId = 3201, Level = LogLevel.Warning, Message = "Rejected a validly signed access token ({Reason})")]
    private static partial void LogTokenRejected(ILogger logger, string reason);
}
