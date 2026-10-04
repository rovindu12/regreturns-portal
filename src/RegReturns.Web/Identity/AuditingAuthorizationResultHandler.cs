using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

using RegReturns.Domain.Auditing;
using RegReturns.Infrastructure.Auditing;

namespace RegReturns.Web.Identity;

/// <summary>
/// Records <see cref="AuditAction.AccessDenied"/> when a signed-in user is refused by a policy (plan §4.6), then lets
/// the default handler respond (redirect to the access-denied page). The <see cref="AccessDeniedAuditor"/> writes at
/// most one entry per user and path per minute. Anonymous requests are challenged, not audited.
/// </summary>
public sealed class AuditingAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    /// <summary>The reason recorded when the endpoint names no policy (the default or fallback policy refused).</summary>
    public const string DefaultPolicyReason = "default";

    private readonly AuthorizationMiddlewareResultHandler _inner = new();

    /// <inheritdoc />
    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true)
        {
            var origin = RequestOrigin.From(context);
            var auditor = context.RequestServices.GetRequiredService<AccessDeniedAuditor>();
            await auditor.RecordAsync(
                context.User, AuditAction.AccessDenied, origin.Path, PolicyNames(context.GetEndpoint()), origin.IpAddress, origin.TraceId,
                context.RequestAborted);
        }

        await _inner.HandleAsync(next, context, policy, authorizeResult);
    }

    /// <summary>Returns the policy names an endpoint requires, comma-separated.</summary>
    /// <param name="endpoint">The endpoint being authorized.</param>
    /// <returns>The policy names, or <see cref="DefaultPolicyReason"/> when it names none.</returns>
    public static string PolicyNames(Endpoint? endpoint)
    {
        var names = endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(data => data.Policy)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];
        return names.Count == 0 ? DefaultPolicyReason : string.Join(',', names);
    }
}
