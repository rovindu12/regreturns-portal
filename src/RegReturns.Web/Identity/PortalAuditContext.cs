using System.Security.Claims;

using RegReturns.Application.Auditing;

namespace RegReturns.Web.Identity;

/// <summary>
/// The portal's <see cref="IAuditContext"/>: the signed-in user, IP address and trace id of the current request
/// (<see cref="RequestOrigin"/>), or <see cref="AuditOrigin.System"/> outside a request. During sign-in the session
/// does not exist yet, so <see cref="SignInProcessor"/> names the user with <see cref="ActAs"/>.
/// </summary>
/// <param name="httpContextAccessor">Gives access to the current request.</param>
public sealed class PortalAuditContext(IHttpContextAccessor httpContextAccessor) : IAuditContext
{
    private AuditActor? _actingAs;

    /// <inheritdoc />
    public AuditOrigin Current
    {
        get
        {
            var http = httpContextAccessor.HttpContext;
            if (http is null)
            {
                return _actingAs is null ? AuditOrigin.System : AuditOrigin.System with { Actor = _actingAs };
            }

            var origin = RequestOrigin.From(http);
            return new AuditOrigin(_actingAs ?? AuditActor.FromPrincipal(http.User), origin.IpAddress, origin.TraceId);
        }
    }

    /// <summary>Attributes changes saved until the returned scope ends to the given principal instead of the request's user.</summary>
    /// <param name="principal">The principal acting, such as the one built from a validated id_token.</param>
    /// <returns>A scope that restores the previous actor when disposed.</returns>
    public IDisposable ActAs(ClaimsPrincipal principal)
    {
        var previous = _actingAs;
        _actingAs = AuditActor.FromPrincipal(principal);
        return new ActingScope(this, previous);
    }

    private sealed class ActingScope(PortalAuditContext context, AuditActor? previous) : IDisposable
    {
        public void Dispose() => context._actingAs = previous;
    }
}
