using System.Text.Json.Nodes;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;
using RegReturns.IamBootstrap.Wso2;
using RegReturns.Infrastructure.Persistence;

namespace RegReturns.IamBootstrap.Steps;

/// <summary>
/// Ensures one client-credentials app per active bank (all API scopes) plus the public Swagger demo client
/// (read-only, one bank, allowed to ask for tokens from the API's origin), and records every client id in
/// <c>iam.ApiClients</c> so the API can scope tokens to a bank. Each registered client gets the client user it acts
/// through (ADR 0026).
/// </summary>
/// <param name="applications">Application management.</param>
/// <param name="db">The RegReturns database.</param>
/// <param name="options">The tool options.</param>
internal sealed class ApiClientsStep(Wso2Applications applications, RegReturnsDbContext db, IOptions<BootstrapOptions> options) : IBootstrapStep
{
    private static readonly string[] BankScopes = [ApiScopes.ReturnsRead, ApiScopes.ReturnsSubmit, ApiScopes.ReferenceRead];

    // The demo client's secret is published on the demo page, so it can read but never submit.
    private static readonly string[] DemoScopes = [ApiScopes.ReturnsRead, ApiScopes.ReferenceRead];

    /// <inheritdoc />
    public string Name => "api-clients";

    /// <inheritdoc />
    public async Task RunAsync(BootstrapState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var apiResourceId = state.ApiResourceId ?? throw new InvalidOperationException("The API resource step must run first.");
        var banks = await db.Institutions.Where(i => i.IsActive).OrderBy(i => i.Code).ToListAsync(cancellationToken);

        foreach (var bank in banks)
        {
            var app = await EnsureClientAsync(
                IamNames.BankApp(bank.Code), IamNames.BankClientId(bank.Code), $"Bank system of {bank.Name} (client credentials).",
                apiResourceId, BankScopes, [], state, cancellationToken);
            await EnsureApiClientAsync(bank, app.ClientId, IamNames.BankApp(bank.Code), state, cancellationToken);
            state.GeneratedSettings[$"BANK_{bank.Code.ToUpperInvariant()}_CLIENT_ID"] = app.ClientId;
            state.GeneratedSettings[$"BANK_{bank.Code.ToUpperInvariant()}_CLIENT_SECRET"] = app.ClientSecret;
        }

        var demoCode = options.Value.DemoApiInstitutionCode;
        var demoBank = banks.SingleOrDefault(b => string.Equals(b.Code, demoCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The demo API institution '{demoCode}' is not an active institution.");
        var demo = await EnsureClientAsync(
            IamNames.DemoApiApp, IamNames.DemoApiClientId, $"Public Swagger demo client, read-only, acting for {demoBank.Name}.",
            apiResourceId, DemoScopes, options.Value.ApiOrigin is { } origin ? [origin] : [], state, cancellationToken);
        await EnsureApiClientAsync(demoBank, demo.ClientId, IamNames.DemoApiApp, state, cancellationToken);
        state.GeneratedSettings["DEMO_API_CLIENT_ID"] = demo.ClientId;
        state.GeneratedSettings["DEMO_API_CLIENT_SECRET"] = demo.ClientSecret;

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Wso2Application> EnsureClientAsync(
        string name,
        string clientId,
        string description,
        string apiResourceId,
        string[] scopes,
        string[] allowedOrigins,
        BootstrapState state,
        CancellationToken ct)
    {
        var settings = new JsonObject { ["description"] = description };
        var createOnly = new JsonObject { ["templateId"] = "m2m-application" };
        var oidc = new JsonObject
        {
            ["grantTypes"] = new JsonArray("client_credentials"),
            ["publicClient"] = false,
            ["callbackURLs"] = new JsonArray(),
            ["allowedOrigins"] = new JsonArray([.. allowedOrigins.Select(o => JsonValue.Create(o))]),
            ["accessToken"] = new JsonObject
            {
                ["type"] = "JWT",
                ["userAccessTokenExpiryInSeconds"] = IamNames.AccessTokenSeconds,
                ["applicationAccessTokenExpiryInSeconds"] = IamNames.AccessTokenSeconds,
            },

            // Puts the API identifier into "aud", which the API validates.
            ["idToken"] = new JsonObject { ["audience"] = new JsonArray(ApiScopes.ApiIdentifier) },
        };

        var app = await applications.EnsureAsync(name, clientId, settings, oidc, createOnly, ct);
        state.Record("M2M app", name, app.Outcome);
        state.Record("authorized API", name, await applications.EnsureAuthorizedApiAsync(app.Id, apiResourceId, scopes, ct));
        return app;
    }

    private async Task EnsureApiClientAsync(Institution institution, string clientId, string name, BootstrapState state, CancellationToken ct)
    {
        var row = await db.ApiClients.SingleOrDefaultAsync(c => c.Wso2ClientId == clientId, ct);
        if (row is null)
        {
            row = ApiClient.Create(institution, clientId, name);
            await db.ApiClients.AddAsync(row, ct);
        }
        else if (row.InstitutionId != institution.Id)
        {
            throw new InvalidOperationException($"API client '{clientId}' is linked to another institution; fix it by hand.");
        }
        else if (!row.IsActive || row.Name != name)
        {
            row.Relink(clientId, name);
        }

        var clientUserId = row.Id;
        if (!await db.Users.AnyAsync(u => u.ApiClientId == clientUserId, ct))
        {
            await db.Users.AddAsync(AppUser.ForApiClient(row), ct);
            state.Record("client user", name, Outcome.Created);
        }
    }
}
