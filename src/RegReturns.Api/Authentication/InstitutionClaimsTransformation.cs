using System.Globalization;
using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;

namespace RegReturns.Api.Authentication;

/// <summary>
/// Gives a client-credentials caller (<c>aut=APPLICATION</c>) the institution its client is registered for in
/// <c>iam.ApiClients</c>: the <c>institution_id</c> code and the internal <see cref="ApiClaimNames.InstitutionKey"/>.
/// Institution claims that arrive in a token are removed first, so only that registry grants an institution, and
/// unknown or inactive clients end up with none, which every API policy refuses. Runs for any authentication scheme,
/// including the hosted-test scheme, and is idempotent.
/// </summary>
/// <param name="lookup">Finds the institution behind a client id.</param>
/// <param name="cache">Remembers lookups, including misses, for <see cref="CacheDuration"/>.</param>
/// <param name="httpContextAccessor">Supplies the request's cancellation token.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class InstitutionClaimsTransformation(
    IQueryHandler<GetApiClientInstitution, ApiClientInstitution?> lookup,
    IMemoryCache cache,
    IHttpContextAccessor httpContextAccessor,
    ILogger<InstitutionClaimsTransformation> logger) : IClaimsTransformation
{
    /// <summary>The label put on the primary identity of a transformed principal.</summary>
    public const string TransformedLabel = "regreturns-api-institution";

    /// <summary>
    /// How long a lookup is reused. Short, so a deactivated client loses access within a minute, but long enough
    /// that a busy client does not query the database on every request.
    /// </summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    /// <inheritdoc />
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.Identity?.IsAuthenticated != true || principal.Identities.Any(i => i.Label == TransformedLabel))
        {
            return principal;
        }

        var clientId = principal.HasClaim(ClaimNames.AuthorizedUserType, ClaimNames.ApplicationTokenType)
            ? ClientIdOf(principal)
            : null;
        var institution = clientId is null ? null : await FindInstitutionAsync(clientId);

        // Never change the authenticated principal in place: the handler may hand the same instance out again.
        var transformed = new ClaimsPrincipal(principal.Identities.Select(WithoutInstitutionClaims));
        var primary = transformed.Identities.First();
        primary.Label = TransformedLabel;
        if (institution is not null)
        {
            primary.AddClaim(new Claim(ClaimNames.InstitutionId, institution.InstitutionCode, ClaimValueTypes.String, ApiClaimNames.Issuer));
            primary.AddClaim(new Claim(
                ApiClaimNames.InstitutionKey,
                institution.InstitutionId.ToString("D", CultureInfo.InvariantCulture),
                ClaimValueTypes.String,
                ApiClaimNames.Issuer));
        }

        return transformed;
    }

    /// <summary>Returns the client id of a client-credentials principal: <c>azp</c>, else <c>client_id</c>.</summary>
    /// <param name="principal">The principal.</param>
    /// <returns>The client id, or <see langword="null"/> if the principal has none.</returns>
    internal static string? ClientIdOf(ClaimsPrincipal principal) =>
        NullIfBlank(principal.FindFirst(ClaimNames.AuthorizedParty)?.Value)
        ?? NullIfBlank(principal.FindFirst(ClaimNames.ClientId)?.Value);

    private static ClaimsIdentity WithoutInstitutionClaims(ClaimsIdentity identity)
    {
        var copy = identity.Clone();
        foreach (var claim in copy.Claims.Where(c => c.Type is ClaimNames.InstitutionId or ApiClaimNames.InstitutionKey).ToList())
        {
            copy.TryRemoveClaim(claim);
        }

        return copy;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private async Task<ApiClientInstitution?> FindInstitutionAsync(string clientId)
    {
        var key = string.Concat("api-client-institution|", clientId);
        if (cache.TryGetValue(key, out CachedLookup? cached) && cached is not null)
        {
            return cached.Institution;
        }

        var cancellationToken = httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var institution = await lookup.HandleAsync(new GetApiClientInstitution(clientId), cancellationToken);
        cache.Set(key, new CachedLookup(institution), CacheDuration);

        if (institution is null)
        {
            LogUnknownClient(logger, clientId);
        }
        else
        {
            LogClientResolved(logger, clientId, institution.InstitutionCode);
        }

        return institution;
    }

    [LoggerMessage(EventId = 3202, Level = LogLevel.Warning,
        Message = "API client {ClientId} is not registered or not active; it gets no institution")]
    private static partial void LogUnknownClient(ILogger logger, string clientId);

    [LoggerMessage(EventId = 3203, Level = LogLevel.Debug, Message = "API client {ClientId} acts for institution {InstitutionCode}")]
    private static partial void LogClientResolved(ILogger logger, string clientId, string institutionCode);

    /// <summary>A cached lookup result; wraps the value so a miss can be cached too.</summary>
    /// <param name="Institution">The institution, or <see langword="null"/> for an unknown or inactive client.</param>
    private sealed record CachedLookup(ApiClientInstitution? Institution);
}
