using RegReturns.Application.Identity;

namespace RegReturns.Api.Authentication;

/// <summary>The API's <see cref="ICurrentClient"/>: the client id of the request's authenticated principal.</summary>
/// <param name="httpContextAccessor">Gives access to the current request.</param>
internal sealed class HttpCurrentClient(IHttpContextAccessor httpContextAccessor) : ICurrentClient
{
    /// <inheritdoc />
    public string? ClientId => httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
        ? InstitutionClaimsTransformation.ClientIdOf(user)
        : null;
}
