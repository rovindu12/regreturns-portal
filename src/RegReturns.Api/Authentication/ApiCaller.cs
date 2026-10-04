using System.Globalization;
using System.Security.Claims;

using RegReturns.Application.Identity;

namespace RegReturns.Api.Authentication;

/// <summary>The authenticated bank client behind an API request.</summary>
/// <param name="ClientId">The OAuth client id.</param>
/// <param name="InstitutionId">The internal id of the institution the client acts for.</param>
/// <param name="InstitutionCode">The code of that institution.</param>
/// <param name="Scopes">The scopes granted to the token, sorted.</param>
internal sealed record ApiCaller(string ClientId, Guid InstitutionId, string InstitutionCode, IReadOnlyList<string> Scopes)
{
    /// <summary>
    /// Reads the caller from a principal enriched by <see cref="InstitutionClaimsTransformation"/>. Only institution
    /// claims issued by the API itself count.
    /// </summary>
    /// <param name="principal">The current principal.</param>
    /// <returns>The caller, or <see langword="null"/> if the principal has no client or no registered institution.</returns>
    public static ApiCaller? FromPrincipal(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var clientId = InstitutionClaimsTransformation.ClientIdOf(principal);
        var code = principal.FindFirst(c => c.Type == ClaimNames.InstitutionId && c.Issuer == ApiClaimNames.Issuer)?.Value;
        var key = principal.FindFirst(c => c.Type == ApiClaimNames.InstitutionKey && c.Issuer == ApiClaimNames.Issuer)?.Value;
        if (clientId is null || string.IsNullOrWhiteSpace(code) || !Guid.TryParse(key, CultureInfo.InvariantCulture, out var institutionId))
        {
            return null;
        }

        var scopes = principal.FindAll(ClaimNames.Scope)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        return new ApiCaller(clientId, institutionId, code, scopes);
    }
}
