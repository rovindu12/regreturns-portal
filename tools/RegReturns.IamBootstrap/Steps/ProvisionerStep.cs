using System.Text.Json.Nodes;

using RegReturns.IamBootstrap.Wso2;

namespace RegReturns.IamBootstrap.Steps;

/// <summary>
/// Ensures the client-credentials app the portal uses to create users, lock them and change their roles over SCIM 2
/// (plan §4.4), authorized for exactly the WSO2 system API scopes that needs and nothing more.
/// </summary>
/// <param name="wso2">The management API client.</param>
/// <param name="applications">Application management.</param>
internal sealed class ProvisionerStep(Wso2AdminClient wso2, Wso2Applications applications) : IBootstrapStep
{
    /// <summary>Environment key of the provisioner's client id in the generated file.</summary>
    public const string ClientIdKey = "PROVISIONER_CLIENT_ID";

    /// <summary>Environment key of the provisioner's client secret in the generated file.</summary>
    public const string ClientSecretKey = "PROVISIONER_CLIENT_SECRET";

    // WSO2 system API resources, looked up by identifier because their ids differ per server. Locking a user is a
    // user update (accountLocked), so no governance or password scopes are granted.
    private static readonly (string Identifier, string[] Scopes)[] SystemApis =
    [
        ("/scim2/Users",
            ["internal_user_mgt_create", "internal_user_mgt_update", "internal_user_mgt_list", "internal_user_mgt_view", "internal_user_mgt_delete"]),
        ("/scim2/Roles", ["internal_role_mgt_view", "internal_role_mgt_users_update"]),
    ];

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

        foreach (var (identifier, scopes) in SystemApis)
        {
            var list = await wso2.GetAsync($"api/server/v1/api-resources?filter=identifier+eq+{Wso2Ids.Filter(identifier)}", cancellationToken);
            var resourceId = list["apiResources"]?.AsArray().FirstOrDefault()?["id"]?.GetValue<string>()
                ?? throw new InvalidOperationException($"WSO2 has no system API resource '{identifier}'.");
            state.Record("authorized API", $"{IamNames.ProvisionerApp} {identifier}",
                await applications.EnsureAuthorizedApiAsync(app.Id, resourceId, scopes, cancellationToken));
        }

        state.GeneratedSettings[ClientIdKey] = app.ClientId;
        state.GeneratedSettings[ClientSecretKey] = app.ClientSecret;
    }
}
