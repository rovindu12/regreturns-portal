using System.Text.Json.Nodes;

using Microsoft.Extensions.Options;

using RegReturns.IamBootstrap.Wso2;

namespace RegReturns.IamBootstrap.Steps;

/// <summary>
/// Closes WSO2's self-service paths so demo users cannot change their password, profile or MFA (plan §4.5): turns off
/// self-registration and account recovery, and disables the My Account app. The portal itself never requests the
/// <c>internal_login</c> scope, so its tokens cannot call WSO2's self-service APIs either.
/// </summary>
/// <param name="wso2">The management API client.</param>
/// <param name="applications">Application management.</param>
/// <param name="options">The tool options.</param>
internal sealed class SelfServiceStep(Wso2AdminClient wso2, Wso2Applications applications, IOptions<BootstrapOptions> options) : IBootstrapStep
{
    /// <summary>The client id of WSO2's built-in My Account app.</summary>
    public const string MyAccountClientId = "MY_ACCOUNT";

    // Identity-governance connectors (category, connector, properties that must be false). Ids are base64url of the names.
    private static readonly (string Category, string Connector, string[] Properties)[] Connectors =
    [
        ("User Onboarding", "self-sign-up", ["SelfRegistration.Enable"]),
        ("User Onboarding", "lite-user-sign-up", ["LiteRegistration.Enable"]),
        ("Account Management", "account-recovery",
            ["Recovery.Notification.Password.Enable", "Recovery.Question.Password.Enable", "Recovery.Notification.Username.Enable"]),
    ];

    /// <inheritdoc />
    public string Name => "self-service";

    /// <inheritdoc />
    public async Task RunAsync(BootstrapState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!options.Value.LockDownSelfService)
        {
            return;
        }

        foreach (var (category, connector, properties) in Connectors)
        {
            state.Record("governance connector", connector, await EnsureDisabledAsync(category, connector, properties, cancellationToken));
        }

        var myAccount = await applications.FindByClientIdAsync(MyAccountClientId, cancellationToken)
            ?? throw new InvalidOperationException("WSO2's My Account app was not found.");
        if (myAccount["applicationEnabled"]?.GetValue<bool>() == false)
        {
            state.Record("application", "My Account (disabled)", Outcome.Unchanged);
            return;
        }

        // Only applicationEnabled is sent: a PATCH carrying associatedRoles would delete My Account's roles.
        await applications.SetEnabledAsync(myAccount["id"]!.GetValue<string>(), enabled: false, cancellationToken);
        state.Record("application", "My Account (disabled)", Outcome.Updated);
    }

    private async Task<Outcome> EnsureDisabledAsync(string category, string connector, string[] properties, CancellationToken cancellationToken)
    {
        var path = $"api/server/v1/identity-governance/{Wso2Ids.ForUri(category)}/connectors/{Wso2Ids.ForUri(connector)}";
        var current = await wso2.GetAsync(path, cancellationToken);
        var values = current["properties"]?.AsArray()
            .Where(p => p?["name"] is not null)
            .ToDictionary(p => p!["name"]!.GetValue<string>(), p => p!["value"]?.ToString(), StringComparer.Ordinal) ?? [];
        var toChange = properties.Where(p => !string.Equals(values.GetValueOrDefault(p), "false", StringComparison.OrdinalIgnoreCase)).ToList();
        if (toChange.Count == 0)
        {
            return Outcome.Unchanged;
        }

        var patch = new JsonObject
        {
            ["operation"] = "UPDATE",
            ["properties"] = new JsonArray([.. toChange.Select(p => new JsonObject { ["name"] = p, ["value"] = "false" })]),
        };
        (await wso2.SendAsync(HttpMethod.Patch, path, patch, Wso2AdminClient.Json, cancellationToken)).EnsureSuccess();
        return Outcome.Updated;
    }
}
