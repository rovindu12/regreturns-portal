using System.Globalization;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Options;

using RegReturns.IamBootstrap.Wso2;

namespace RegReturns.IamBootstrap.Steps;

/// <summary>
/// Locks a WSO2 account after repeated failed sign-ins (ADR 0033), which WSO2 7.3 leaves off: after
/// <see cref="BootstrapOptions.FailedSignInsBeforeLock"/> wrong passwords or codes in a row the account is locked for
/// <see cref="BootstrapOptions.AccountLockMinutes"/> minutes, and the same again each time (no growth), so a visitor who
/// mistypes the shared demo password cannot lock a demo account for long. WSO2's lock e-mails are off: there is no mail
/// server.
/// </summary>
/// <param name="wso2">The management API client.</param>
/// <param name="options">The tool options.</param>
internal sealed class AccountLockStep(Wso2AdminClient wso2, IOptions<BootstrapOptions> options) : IBootstrapStep
{
    /// <summary>The identity-governance category.</summary>
    public const string Category = "Login Attempts Security";

    /// <summary>The connector.</summary>
    public const string Connector = "account.lock.handler";

    /// <inheritdoc />
    public string Name => "account-lock";

    /// <inheritdoc />
    public async Task RunAsync(BootstrapState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var settings = options.Value;
        var desired = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["account.lock.handler.lock.on.max.failed.attempts.enable"] = "true",
            ["account.lock.handler.On.Failure.Max.Attempts"] = settings.FailedSignInsBeforeLock.ToString(CultureInfo.InvariantCulture),
            ["account.lock.handler.Time"] = settings.AccountLockMinutes.ToString(CultureInfo.InvariantCulture),
            ["account.lock.handler.login.fail.timeout.ratio"] = "1",
            ["account.lock.handler.notification.manageInternally"] = "false",
        };

        var path = $"api/server/v1/identity-governance/{Wso2Ids.ForUri(Category)}/connectors/{Wso2Ids.ForUri(Connector)}";
        var current = (await wso2.GetAsync(path, cancellationToken))["properties"]?.AsArray()
            .Where(p => p?["name"] is not null)
            .ToDictionary(p => p!["name"]!.GetValue<string>(), p => p!["value"]?.ToString(), StringComparer.Ordinal) ?? [];
        var changes = desired.Where(p => !string.Equals(current.GetValueOrDefault(p.Key), p.Value, StringComparison.OrdinalIgnoreCase)).ToList();
        if (changes.Count == 0)
        {
            state.Record("governance connector", Connector, Outcome.Unchanged);
            return;
        }

        var patch = new JsonObject
        {
            ["operation"] = "UPDATE",
            ["properties"] = new JsonArray([.. changes.Select(p => new JsonObject { ["name"] = p.Key, ["value"] = p.Value })]),
        };
        (await wso2.SendAsync(HttpMethod.Patch, path, patch, Wso2AdminClient.Json, cancellationToken)).EnsureSuccess();
        state.Record("governance connector", Connector, Outcome.Updated);
    }
}
