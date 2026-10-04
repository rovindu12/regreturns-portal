using System.Security.Claims;
using System.Text.Json;

using Microsoft.IdentityModel.JsonWebTokens;

using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;

namespace RegReturns.Web.Identity;

/// <summary>
/// Turns the claims of a WSO2 id_token into the small, predictable principal the portal keeps in its cookie
/// (plan §4.3): only <c>sub</c>, <c>username</c>, <c>name</c>, <c>email</c>, known <c>roles</c>, <c>institution_id</c>,
/// <c>amr</c> and <c>sid</c> survive. Everything else (audiences, hashes, nonces, unknown roles) is dropped.
/// </summary>
public static class PortalClaims
{
    /// <summary>Authentication type of the normalised identity.</summary>
    public const string AuthenticationType = "WSO2";

    private static readonly char[] RoleSeparators = [' ', ','];

    /// <summary>Normalises the claims of a validated id_token.</summary>
    /// <param name="principal">The principal built from the id_token (inbound claim mapping off).</param>
    /// <returns>The normalised claims and the roles that were dropped.</returns>
    public static NormalizedClaims Normalize(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var subject = First(principal, ClaimNames.Subject);
        var userName = First(principal, ClaimNames.UserName);
        var name = First(principal, ClaimNames.Name)
            ?? NullIfBlank(string.Join(' ', new[] { First(principal, JwtRegisteredClaimNames.GivenName), First(principal, JwtRegisteredClaimNames.FamilyName) }
                .Where(part => part is not null)))
            ?? userName;
        var email = First(principal, ClaimNames.Email);
        var institution = First(principal, ClaimNames.InstitutionId);
        var sessionId = First(principal, ClaimNames.SessionId);
        var methods = principal.FindAll(ClaimNames.AuthenticationMethods)
            .Select(claim => claim.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var roles = new List<Role>();
        var unknownRoles = new List<string>();
        foreach (var value in principal.FindAll(ClaimNames.Roles).SelectMany(claim => SplitRoles(claim.Value)))
        {
            if (RoleNames.TryParse(value, out var role))
            {
                if (!roles.Contains(role.Value))
                {
                    roles.Add(role.Value);
                }
            }
            else if (!unknownRoles.Contains(value, StringComparer.Ordinal))
            {
                unknownRoles.Add(value);
            }
        }

        roles.Sort();

        var claims = new List<Claim>();
        Add(claims, ClaimNames.Subject, subject);
        Add(claims, ClaimNames.UserName, userName);
        Add(claims, ClaimNames.Name, name);
        Add(claims, ClaimNames.Email, email);
        claims.AddRange(roles.Select(role => new Claim(ClaimNames.Roles, RoleNames.For(role))));
        Add(claims, ClaimNames.InstitutionId, institution);
        claims.AddRange(methods.Select(method => new Claim(ClaimNames.AuthenticationMethods, method)));
        Add(claims, ClaimNames.SessionId, sessionId);

        var identity = new ClaimsIdentity(claims, AuthenticationType, ClaimNames.Name, ClaimNames.Roles);
        return new NormalizedClaims(
            new ClaimsPrincipal(identity), subject, userName, name, email, institution, roles, methods, sessionId, unknownRoles);
    }

    /// <summary>
    /// Splits one <c>roles</c> claim value. WSO2 sends a single role as a plain string and several as a JSON array
    /// (one claim per element); a JSON array string or a space- or comma-separated list is accepted defensively.
    /// </summary>
    /// <param name="value">The claim value.</param>
    /// <returns>The role names in the value.</returns>
    internal static IReadOnlyList<string> SplitRoles(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return [.. document.RootElement.EnumerateArray()
                        .Where(element => element.ValueKind == JsonValueKind.String)
                        .Select(element => element.GetString()!.Trim())
                        .Where(role => role.Length > 0)];
                }
            }
            catch (JsonException)
            {
                // Not JSON after all: fall back to splitting on separators.
            }
        }

        return trimmed.Split(RoleSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? First(ClaimsPrincipal principal, string type) =>
        principal.FindAll(type).Select(claim => NullIfBlank(claim.Value)).FirstOrDefault(value => value is not null);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Add(List<Claim> claims, string type, string? value)
    {
        if (value is not null)
        {
            claims.Add(new Claim(type, value));
        }
    }
}

/// <summary>The outcome of <see cref="PortalClaims.Normalize"/>.</summary>
/// <param name="Principal">The principal to keep in the session cookie.</param>
/// <param name="Subject">The WSO2 user id (<c>sub</c>), if present.</param>
/// <param name="UserName">The WSO2 user name, if present.</param>
/// <param name="Name">The display name: <c>name</c>, else given and family name, else the user name.</param>
/// <param name="Email">The e-mail address, if present.</param>
/// <param name="InstitutionCode">The <c>institution_id</c> claim, for bank users.</param>
/// <param name="Roles">The known roles, in <see cref="Role"/> order.</param>
/// <param name="AuthenticationMethods">The <c>amr</c> values.</param>
/// <param name="SessionId">The WSO2 session id (<c>sid</c>), if present.</param>
/// <param name="UnknownRoles">Role values that are not RegReturns roles and were dropped.</param>
public sealed record NormalizedClaims(
    ClaimsPrincipal Principal,
    string? Subject,
    string? UserName,
    string? Name,
    string? Email,
    string? InstitutionCode,
    IReadOnlyList<Role> Roles,
    IReadOnlyList<string> AuthenticationMethods,
    string? SessionId,
    IReadOnlyList<string> UnknownRoles);
