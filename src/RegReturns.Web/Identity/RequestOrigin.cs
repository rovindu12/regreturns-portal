using RegReturns.ServiceDefaults.Web;

namespace RegReturns.Web.Identity;

/// <summary>Where a request came from, as recorded in the audit trail.</summary>
/// <param name="Path">The request path without the query string.</param>
/// <param name="IpAddress">The caller's IP address, if known.</param>
/// <param name="TraceId">The W3C trace id, which joins the audit entry to the logs.</param>
public sealed record RequestOrigin(string Path, string? IpAddress, string TraceId)
{
    /// <summary>Reads the origin of the current request.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The origin.</returns>
    public static RequestOrigin From(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var path = (context.Request.PathBase + context.Request.Path).Value;
        return new RequestOrigin(
            string.IsNullOrEmpty(path) ? "/" : path,
            context.Connection.RemoteIpAddress?.ToString(),
            WebDefaultsExtensions.CurrentTraceId(context));
    }
}
