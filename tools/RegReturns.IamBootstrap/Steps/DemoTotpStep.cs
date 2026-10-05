using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Options;

using RegReturns.IamBootstrap.Wso2;
using RegReturns.Infrastructure.Identity.Wso2;

namespace RegReturns.IamBootstrap.Steps;

/// <summary>
/// Pre-enrols TOTP for the demo users who always get the second factor (<see cref="BootstrapOptions.MfaAlwaysUsers"/>),
/// so the demo can publish their secret and the smoke test can compute codes.
/// </summary>
/// <remarks>
/// WSO2 stores TOTP secrets encrypted with its own key, so an administrator cannot set a chosen secret. The step
/// enrols as the user instead: a temporary password-grant app gets the user an <c>internal_login</c> token, WSO2 generates
/// the secret (<c>INIT</c>), one computed code activates it (<c>VALIDATE</c>), and the app is deleted again. A known,
/// still-active secret is kept, so re-runs and demo resets do not force visitors to re-scan the QR code.
/// </remarks>
/// <param name="applications">Application management.</param>
/// <param name="scim">SCIM user management.</param>
/// <param name="httpClientFactory">Creates the back-channel client for the user's own calls.</param>
/// <param name="wso2Options">WSO2 addresses.</param>
/// <param name="options">The tool options.</param>
/// <param name="timeProvider">The clock, for TOTP codes.</param>
internal sealed class DemoTotpStep(
    Wso2Applications applications,
    Wso2Scim scim,
    IHttpClientFactory httpClientFactory,
    IOptions<Wso2Options> wso2Options,
    IOptions<BootstrapOptions> options,
    TimeProvider timeProvider) : IBootstrapStep
{
    /// <summary>The step name, used by the <c>demo-users</c> command.</summary>
    public const string StepName = "demo-totp";

    private const string HelperApp = "RegReturns TOTP Enrolment Helper";
    private const string HelperClientId = "regreturns-totp-enrolment";
    private const string TotpPath = "api/users/v1/me/totp";

    /// <inheritdoc />
    public string Name => StepName;

    /// <summary>The generated-settings key that holds a user's TOTP secret.</summary>
    /// <param name="userName">The user name.</param>
    /// <returns>For example <c>TOTP_SECRET_APPROVER_MFA</c>.</returns>
    public static string SecretKey(string userName) =>
        "TOTP_SECRET_" + new string([.. userName.ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')]);

    /// <inheritdoc />
    public async Task RunAsync(BootstrapState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var settings = options.Value;
        Wso2Application? helper = null;
        try
        {
            foreach (var userName in settings.MfaAlwaysUsers.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var user = await scim.FindUserAsync(userName, cancellationToken)
                    ?? throw new InvalidOperationException($"MFA demo user '{userName}' does not exist; the demo-users step creates it.");
                var key = SecretKey(userName);
                var known = state.ExistingSettings.GetValueOrDefault(key);

                // Trust a known secret on a normal run; a demo reset checks it against WSO2 as the user.
                if (known is not null && !state.ResetDemoUsers &&
                    await scim.IsTotpEnabledAsync(user["id"]!.GetValue<string>(), cancellationToken))
                {
                    state.GeneratedSettings[key] = known;
                    state.Record("TOTP enrolment", userName, Outcome.Unchanged);
                    continue;
                }

                helper ??= await CreateHelperAsync(cancellationToken);
                using var http = await SignInAsUserAsync(helper, userName, settings.DemoUserPassword!, cancellationToken);
                if (known is not null && string.Equals(await CurrentSecretAsync(http, cancellationToken), known, StringComparison.Ordinal))
                {
                    state.GeneratedSettings[key] = known;
                    state.Record("TOTP enrolment", userName, Outcome.Unchanged);
                    continue;
                }

                state.GeneratedSettings[key] = await EnrolAsync(http, cancellationToken);
                state.Record("TOTP enrolment", userName, known is null ? Outcome.Created : Outcome.Updated);
            }
        }
        finally
        {
            if (helper is not null)
            {
                await applications.DeleteAsync(helper.Id, CancellationToken.None);
            }
        }
    }

    private async Task<Wso2Application> CreateHelperAsync(CancellationToken cancellationToken)
    {
        // A leftover from an interrupted run is reused and deleted at the end like a new one.
        var settings = new JsonObject { ["description"] = "Created and deleted by regreturns-iam-bootstrap to enrol demo TOTP." };
        var oidc = new JsonObject
        {
            ["grantTypes"] = new JsonArray("password"),
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
        return await applications.EnsureAsync(HelperApp, HelperClientId, settings, oidc, createOnly: null, cancellationToken);
    }

    private async Task<HttpClient> SignInAsUserAsync(Wso2Application helper, string userName, string password, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient(Wso2Backchannel.HttpClientName);
        http.BaseAddress = wso2Options.Value.Authority;
        using var request = new HttpRequestMessage(HttpMethod.Post, "oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = userName,
                ["password"] = password,
                ["scope"] = "internal_login",
            }),
        };
        request.Headers.Authorization = Wso2AdminClient.BasicCredentials(helper.ClientId, helper.ClientSecret);
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        var token = body?["access_token"]?.GetValue<string>();
        if (!response.IsSuccessStatusCode || token is null)
        {
            http.Dispose();
            throw new InvalidOperationException(
                $"WSO2 refused a token for '{userName}' ({(int)response.StatusCode} {body?["error"]}); check DEMO_USER_PASSWORD.");
        }

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private static async Task<string?> CurrentSecretAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri($"{TotpPath}/secret", UriKind.Relative), cancellationToken);
        return response.IsSuccessStatusCode
            ? (await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))?["secret"]?.GetValue<string>()
            : null;
    }

    private async Task<string> EnrolAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var init = await PostTotpAsync(http, new JsonObject { ["action"] = "INIT" }, cancellationToken);
        var otpAuthUri = Encoding.UTF8.GetString(Convert.FromBase64String(init["qrCodeUrl"]!.GetValue<string>()));
        var secret = Totp.SecretFromUri(otpAuthUri) ?? throw new InvalidOperationException("WSO2 returned no TOTP secret.");

        // INIT only stages the secret; WSO2 asks for it at sign-in once one code has been validated.
        var code = Totp.Code(secret, timeProvider.GetUtcNow());
        var validate = await PostTotpAsync(http, new JsonObject { ["action"] = "VALIDATE", ["verificationCode"] = code }, cancellationToken);
        return validate["isValid"]?.GetValue<bool>() == true
            ? secret
            : throw new InvalidOperationException("WSO2 did not accept the computed TOTP code; check the clocks of this machine and WSO2.");
    }

    private static async Task<JsonObject> PostTotpAsync(HttpClient http, JsonObject body, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(new Uri(TotpPath, UriKind.Relative), body, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return response.IsSuccessStatusCode && result is not null
            ? result
            : throw new InvalidOperationException($"WSO2 TOTP {body["action"]} failed with {(int)response.StatusCode}.");
    }
}
