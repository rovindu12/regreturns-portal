namespace RegReturns.Api.Authentication;

/// <summary>
/// Claims the API adds to a caller's principal itself, after looking the client up in <c>iam.ApiClients</c>.
/// They are never taken from a token.
/// </summary>
internal static class ApiClaimNames
{
    /// <summary>The internal id (a <see cref="Guid"/>) of the institution the client acts for.</summary>
    public const string InstitutionKey = "regreturns:institution_key";

    /// <summary>The issuer recorded on every claim the API adds, which tells them apart from token claims.</summary>
    public const string Issuer = "RegReturns.Api";
}
