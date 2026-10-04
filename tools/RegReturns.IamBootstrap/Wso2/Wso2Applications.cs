using System.Security.Cryptography;
using System.Text.Json.Nodes;

using RegReturns.IamBootstrap.Steps;

namespace RegReturns.IamBootstrap.Wso2;

/// <summary>A WSO2 application and its OAuth credentials.</summary>
/// <param name="Id">The application id.</param>
/// <param name="ClientId">The OAuth client id.</param>
/// <param name="ClientSecret">The OAuth client secret (never logged).</param>
/// <param name="Outcome">What the ensure call did.</param>
internal sealed record Wso2Application(string Id, string ClientId, string ClientSecret, Outcome Outcome);

/// <summary>
/// Idempotent application management, following the verified rules: look up by name, POST the full body when missing,
/// otherwise PUT the complete OIDC object (it replaces everything) and PATCH the top-level settings. Never PATCHes
/// <c>associatedRoles</c>, which would delete the app's roles.
/// </summary>
/// <param name="wso2">The WSO2 admin client.</param>
internal sealed class Wso2Applications(Wso2AdminClient wso2)
{
    private const string Applications = "api/server/v1/applications";

    /// <summary>Creates or updates an application.</summary>
    /// <param name="name">The application name (lookup key, case-insensitive in WSO2).</param>
    /// <param name="clientId">The client id to use when creating it.</param>
    /// <param name="settings">Top-level settings sent on create and PATCHed on update (no roles, no protocols).</param>
    /// <param name="oidc">The complete desired OIDC inbound configuration, without client id or secret.</param>
    /// <param name="createOnly">Settings only sent on create, such as <c>associatedRoles</c> and <c>templateId</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The application.</returns>
    public async Task<Wso2Application> EnsureAsync(
        string name, string clientId, JsonObject settings, JsonObject oidc, JsonObject? createOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(oidc);
        var existing = await FindAsync(name, cancellationToken);
        if (existing is null)
        {
            var body = (JsonObject)settings.DeepClone();
            body["name"] = name;
            foreach (var (key, value) in createOnly ?? [])
            {
                body[key] = value?.DeepClone();
            }

            var inbound = (JsonObject)oidc.DeepClone();
            inbound["clientId"] = clientId;
            inbound["clientSecret"] = NewSecret();
            body["inboundProtocolConfiguration"] = new JsonObject { ["oidc"] = inbound };

            var created = (await wso2.SendAsync(HttpMethod.Post, Applications, body, Wso2AdminClient.Json, cancellationToken)).EnsureSuccess();
            var id = created.LocationId ?? throw new Wso2ApiException(created);
            var credentials = await GetCredentialsAsync(id, cancellationToken);
            return new Wso2Application(id, credentials.ClientId, credentials.ClientSecret, Outcome.Created);
        }

        var appId = existing["id"]!.GetValue<string>();
        var current = await wso2.GetAsync($"{Applications}/{appId}/inbound-protocols/oidc", cancellationToken);
        var existingClientId = current["clientId"]!.GetValue<string>();

        var desiredOidc = (JsonObject)oidc.DeepClone();
        desiredOidc["clientId"] = existingClientId;
        var app = await wso2.GetAsync($"{Applications}/{appId}", cancellationToken);
        var changed = false;
        if (!OidcMatches(current, desiredOidc))
        {
            // Omitting clientSecret keeps the current secret.
            (await wso2.SendAsync(HttpMethod.Put, $"{Applications}/{appId}/inbound-protocols/oidc", desiredOidc, Wso2AdminClient.Json, cancellationToken))
                .EnsureSuccess();
            changed = true;
        }

        if (!Contains(app, settings))
        {
            (await wso2.SendAsync(HttpMethod.Patch, $"{Applications}/{appId}", settings, Wso2AdminClient.Json, cancellationToken)).EnsureSuccess();
            changed = true;
        }

        var secret = current["clientSecret"]?.GetValue<string>();
        if (string.IsNullOrEmpty(secret))
        {
            // Secret hashing is on (the secret cannot be read back): rotate it so the env file is complete.
            var rotated = (await wso2.SendAsync(
                HttpMethod.Post, $"{Applications}/{appId}/inbound-protocols/oidc/regenerate-secret", null, Wso2AdminClient.Json, cancellationToken))
                .EnsureSuccess();
            secret = rotated.Body?["clientSecret"]?.GetValue<string>() ?? throw new Wso2ApiException(rotated);
            changed = true;
        }

        return new Wso2Application(appId, existingClientId, secret, changed ? Outcome.Updated : Outcome.Unchanged);
    }

    /// <summary>Finds an application by exact name.</summary>
    /// <param name="name">The name.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The list item, or <see langword="null"/>.</returns>
    public async Task<JsonNode?> FindAsync(string name, CancellationToken cancellationToken)
    {
        var list = await wso2.GetAsync(
            $"{Applications}?filter=name+eq+{Wso2Ids.Filter(name)}&attributes=clientId,applicationEnabled", cancellationToken);
        return list["applications"]?.AsArray()
            .FirstOrDefault(a => string.Equals(a?["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Authorizes an API resource's scopes for an application, adding and removing scopes to match.</summary>
    /// <param name="appId">The application id.</param>
    /// <param name="apiResourceId">The API resource id (not its identifier URL).</param>
    /// <param name="scopes">The exact scopes the app should have.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What happened.</returns>
    public async Task<Outcome> EnsureAuthorizedApiAsync(
        string appId, string apiResourceId, IReadOnlyCollection<string> scopes, CancellationToken cancellationToken)
    {
        var path = $"{Applications}/{appId}/authorized-apis";
        var authorized = await wso2.GetAsync(path, cancellationToken);
        var entry = authorized.AsArray().FirstOrDefault(a => a?["id"]?.GetValue<string>() == apiResourceId);
        if (entry is null)
        {
            var body = new JsonObject
            {
                ["id"] = apiResourceId,
                ["policyIdentifier"] = "RBAC",
                ["scopes"] = new JsonArray([.. scopes.Select(s => JsonValue.Create(s))]),
            };
            (await wso2.SendAsync(HttpMethod.Post, path, body, Wso2AdminClient.Json, cancellationToken)).EnsureSuccess();
            return Outcome.Created;
        }

        var have = entry["authorizedScopes"]?.AsArray().Select(s => s!["name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
        var added = scopes.Where(s => !have.Contains(s)).ToList();
        var removed = have.Where(s => !scopes.Contains(s, StringComparer.Ordinal)).ToList();
        if (added.Count == 0 && removed.Count == 0)
        {
            return Outcome.Unchanged;
        }

        var patch = new JsonObject
        {
            ["addedScopes"] = new JsonArray([.. added.Select(s => JsonValue.Create(s))]),
            ["removedScopes"] = new JsonArray([.. removed.Select(s => JsonValue.Create(s))]),
        };
        (await wso2.SendAsync(HttpMethod.Patch, $"{path}/{apiResourceId}", patch, Wso2AdminClient.Json, cancellationToken)).EnsureSuccess();
        return Outcome.Updated;
    }

    /// <summary>Enables or disables an application without touching anything else.</summary>
    /// <param name="appId">The application id.</param>
    /// <param name="enabled">Whether it should be enabled.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when done.</returns>
    public async Task SetEnabledAsync(string appId, bool enabled, CancellationToken cancellationToken) =>
        (await wso2.SendAsync(
            HttpMethod.Patch, $"{Applications}/{appId}", new JsonObject { ["applicationEnabled"] = enabled }, Wso2AdminClient.Json, cancellationToken))
            .EnsureSuccess();

    /// <summary>Creates a random client secret (256 bits, base64url).</summary>
    /// <returns>The secret.</returns>
    public static string NewSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<(string ClientId, string ClientSecret)> GetCredentialsAsync(string appId, CancellationToken cancellationToken)
    {
        var oidc = await wso2.GetAsync($"{Applications}/{appId}/inbound-protocols/oidc", cancellationToken);
        return (oidc["clientId"]!.GetValue<string>(), oidc["clientSecret"]?.GetValue<string>() ?? string.Empty);
    }

    /// <summary>True when every desired OIDC field is already present with the same value (WSO2 adds extra fields).</summary>
    private static bool OidcMatches(JsonNode current, JsonObject desired) => Contains(current, desired);

    /// <summary>Returns whether <paramref name="actual"/> contains every property of <paramref name="expected"/>, recursively.</summary>
    internal static bool Contains(JsonNode? actual, JsonNode? expected)
    {
        switch (expected)
        {
            case JsonObject expectedObject:
                return actual is JsonObject actualObject &&
                    expectedObject.All(p => Contains(actualObject[p.Key], p.Value));
            case JsonArray expectedArray:
                // Arrays from WSO2 may come back in another order: compare as multisets.
                return actual is JsonArray actualArray &&
                    actualArray.Count == expectedArray.Count &&
                    MatchEach(expectedArray, 0, actualArray, []);
            case null:
                return actual is null;
            default:
                return JsonNode.DeepEquals(actual, expected);
        }
    }

    /// <summary>
    /// Pairs every expected item from <paramref name="index"/> on with a distinct unused actual item. An item can fit
    /// several candidates (subset match), so a greedy pick could fail where a full pairing exists: backtrack instead.
    /// </summary>
    private static bool MatchEach(JsonArray expected, int index, JsonArray actual, HashSet<int> used)
    {
        if (index == expected.Count)
        {
            return true;
        }

        for (var i = 0; i < actual.Count; i++)
        {
            if (!used.Contains(i) && Contains(actual[i], expected[index]))
            {
                used.Add(i);
                if (MatchEach(expected, index + 1, actual, used))
                {
                    return true;
                }

                used.Remove(i);
            }
        }

        return false;
    }
}
