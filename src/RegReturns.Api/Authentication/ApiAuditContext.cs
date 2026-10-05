using RegReturns.Application.Auditing;
using RegReturns.ServiceDefaults.Web;

namespace RegReturns.Api.Authentication;

/// <summary>
/// The API's <see cref="IAuditContext"/>: the calling client (or user), IP address and trace id of the current request,
/// or <see cref="AuditOrigin.System"/> outside a request.
/// </summary>
/// <param name="httpContextAccessor">Gives access to the current request.</param>
internal sealed class ApiAuditContext(IHttpContextAccessor httpContextAccessor) : IAuditContext
{
    /// <inheritdoc />
    public AuditOrigin Current => httpContextAccessor.HttpContext is { } http
        ? new AuditOrigin(
            AuditActor.FromPrincipal(http.User),
            http.Connection.RemoteIpAddress?.ToString(),
            WebDefaultsExtensions.CurrentTraceId(http))
        : AuditOrigin.System;
}
