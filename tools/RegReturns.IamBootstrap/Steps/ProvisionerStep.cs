using System.Text.Json.Nodes;

using RegReturns.IamBootstrap.Wso2;
using RegReturns.Infrastructure.Identity.Wso2;

namespace RegReturns.IamBootstrap.Steps;

/// <summary>
/// Ensures the client-credentials app the portal uses over SCIM 2 to open an authenticator reset (ADR 0032), authorized
/// for exactly the scopes the portal requests (<see cref="Wso2IdentityDirectory.Scopes"/>) and nothing more.
/// </summary>
/// <param name="wso2">The management API client.</param>
/// <param name="applications">Application management.</param>
internal sealed class ProvisionerStep(Wso2AdminClient wso2, Wso2Applications applications) : IBootstrapStep
{
    /// <summary>Environment key of the provisioner's client id in the generated file.</summary>
    public const string ClientIdKey = "PROVISIONER_CLIENT_ID";

    /// <summary>Environment key of the provisioner's client secret in the generated file.</summary>
    public const string ClientSecretKey = "PROVISIONER_CLIENT_SECRET";

    // WSO2 system API resources, looked up by identifier because their ids differ per server.
    private const string UsersApi = "/scim2/Users";

    // Earlier versions also authorized creating and deleting users and changing role members, for a provisioning
    // feature that was never built; an apply withdraws what is left of that.
    private static readonly string[] WithdrawnApis = ["/scim2/Roles"];

    /// <summary>The scopes authorized on the users API: those the portal requests, no more.</summary>
    internal static IReadOnlyList<string> UserScopes { get; } = Wso2IdentityDirectory.Scopes.Split(' ');

    /// <inheritdoc />
    public string Name => "provisioner";

    /// <inheritdoc />
    public async Task RunAsync(BootstrapState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var settings = new JsonObject { ["description"] = "Portal user provisioning over SCIM 2 (client credentials)." };
        var createOnly = new JsonObject { ["templateId"] = "m2m-application" };
        var oidc = new JsonObject
        {
            ["grantTypes"] = new JsonArray("client_credentials"),
            ["publicClient"] = false,
            ["callbackURLs"] = new JsonArray(),
            ["allowedOrigins"] = new JsonArray(),
            ["accessToken"] = new JsonObject
            {
                ["type"] = "JWT",
                ["userAccessTokenExpiryInSeconds"] = IamNames.AccessTokenSeconds,
                ["applicationAccessTokenExpiryInSeconds"] = IamNames.AccessTokenSeconds,
            },
        };

        var app = await applications.EnsureAsync(IamNames.ProvisionerApp, IamNames.ProvisionerClientId, settings, oidc, createOnly, cancellationToken);
        state.Record("M2M app", IamNames.ProvisionerApp, app.Outcome);

        var usersApi = await FindApiResourceAsync(UsersApi, cancellationToken)
            ?? throw new InvalidOperationException($"WSO2 has no system API resource '{UsersApi}'.");
        state.Record("authorized API", $"{IamNames.ProvisionerApp} {UsersApi}",
            await applications.EnsureAuthorizedApiAsync(app.Id, usersApi, [.. UserScopes], cancellationToken));

        foreach (var identifier in WithdrawnApis)
        {
            if (await FindApiResourceAsync(identifier, cancellationToken) is { } resourceId
                && await applications.RevokeAuthorizedApiAsync(app.Id, resourceId, cancellationToken) == Outcome.Updated)
            {
                state.Record("withdrawn API", $"{IamNames.ProvisionerApp} {identifier}", Outcome.Updated);
            }
        }

        state.GeneratedSettings[ClientIdKey] = app.ClientId;
        state.GeneratedSettings[ClientSecretKey] = app.ClientSecret;
    }

    private async Task<string?> FindApiResourceAsync(string identifier, CancellationToken cancellationToken)
    {
        var list = await wso2.GetAsync($"api/server/v1/api-resources?filter=identifier+eq+{Wso2Ids.Filter(identifier)}", cancellationToken);
        return list["apiResources"]?.AsArray().FirstOrDefault()?["id"]?.GetValue<string>();
    }
}
