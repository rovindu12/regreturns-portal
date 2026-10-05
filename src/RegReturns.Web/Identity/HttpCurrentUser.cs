using RegReturns.Application.Abstractions;
using RegReturns.Application.Identity;

namespace RegReturns.Web.Identity;

/// <summary>Reads the signed-in user's WSO2 subject id from the portal session of the current request.</summary>
/// <param name="httpContextAccessor">Gives access to the current request.</param>
public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    /// <inheritdoc />
    public string? SubjectId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            return user?.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimNames.Subject)?.Value : null;
        }
    }
}
