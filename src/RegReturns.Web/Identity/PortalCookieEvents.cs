using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

using RegReturns.Application.Identity;

namespace RegReturns.Web.Identity;

/// <summary>
/// Session cookie events: stamps the 8-hour absolute expiry at sign-in, and on every request refuses a session that
/// has passed it or whose WSO2 session was ended by back-channel logout.
/// </summary>
/// <param name="denyList">The ended WSO2 sessions.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed partial class PortalCookieEvents(
    ISessionDenyList denyList,
    TimeProvider timeProvider,
    ILogger<PortalCookieEvents> logger) : CookieAuthenticationEvents
{
    /// <inheritdoc />
    public override Task SigningIn(CookieSigningInContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PortalSession.SetAbsoluteExpiry(context.Properties, timeProvider.GetUtcNow() + PortalSession.AbsoluteLifetime);
        return base.SigningIn(context);
    }

    /// <inheritdoc />
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (PortalSession.HasExpired(context.Properties, timeProvider.GetUtcNow()))
        {
            LogSessionExpired(logger, context.Principal?.FindFirst(ClaimNames.Subject)?.Value);
            await RejectAsync(context);
            return;
        }

        var sessionId = context.Principal?.FindFirst(ClaimNames.SessionId)?.Value;
        if (!string.IsNullOrEmpty(sessionId) && await denyList.IsDeniedAsync(sessionId, context.HttpContext.RequestAborted))
        {
            LogSessionEnded(logger, context.Principal?.FindFirst(ClaimNames.Subject)?.Value);
            await RejectAsync(context);
            return;
        }

        await base.ValidatePrincipal(context);
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }

    [LoggerMessage(EventId = 3105, Level = LogLevel.Information, Message = "Session of {SubjectId} reached its absolute expiry")]
    private static partial void LogSessionExpired(ILogger logger, string? subjectId);

    [LoggerMessage(EventId = 3106, Level = LogLevel.Information, Message = "Session of {SubjectId} was ended by WSO2 back-channel logout")]
    private static partial void LogSessionEnded(ILogger logger, string? subjectId);
}
