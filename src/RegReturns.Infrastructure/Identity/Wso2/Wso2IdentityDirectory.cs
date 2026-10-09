using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

using RegReturns.Application.Identity;

namespace RegReturns.Infrastructure.Identity.Wso2;

/// <summary>
/// The provisioner client the portal uses for SCIM 2 calls to WSO2 (section <c>Iam:Provisioner</c>, ADR 0032).
/// IamBootstrap creates it and writes its credentials to <c>.env.generated</c> (<c>PROVISIONER_CLIENT_ID</c> and
/// <c>PROVISIONER_CLIENT_SECRET</c>).
/// </summary>
public sealed class Wso2ProvisionerOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Iam:Provisioner";

    /// <summary>Gets or sets the client id.</summary>
    [MaxLength(128)]
    public string? ClientId { get; set; }

    /// <summary>Gets or sets the client secret. Secret: user-secrets or the environment only.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Gets or sets how long one call to WSO2, token included, may take.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>Gets a value indicating whether both credentials are set.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// <see cref="IIdentityDirectory"/> over WSO2's SCIM 2 API, called server to server through the WSO2 back channel
/// (never through the public edge, which refuses <c>/scim2</c>). The provisioner's token comes from the client
/// credentials grant with only the user scopes these calls need, and is reused until a minute before it expires.
/// </summary>
/// <param name="httpClientFactory">Creates the back-channel client.</param>
/// <param name="wso2">Where WSO2 is.</param>
/// <param name="provisioner">The provisioner's credentials.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class Wso2IdentityDirectory(
    IHttpClientFactory httpClientFactory,
    IOptions<Wso2Options> wso2,
    IOptions<Wso2ProvisionerOptions> provisioner,
    TimeProvider timeProvider) : IIdentityDirectory, IDisposable
{
    /// <summary>The scopes the token is requested with: find and read users, and update one attribute.</summary>
    public const string Scopes = "internal_user_mgt_list internal_user_mgt_view internal_user_mgt_update";

    private const string ScimJson = "application/scim+json";
    private const string PatchOpSchema = "urn:ietf:params:scim:api:messages:2.0:PatchOp";
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpires;

    /// <inheritdoc />
    public Task<IdentityAccount?> FindByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        return CallAsync(
            async (client, token, ct) =>
            {
                var filter = Uri.EscapeDataString($"userName eq \"{userName.Replace("\"", string.Empty, StringComparison.Ordinal)}\"");
                using var request = ScimRequest(HttpMethod.Get, $"?filter={filter}&attributes=userName,roles", token);
                using var response = await SendAsync(client, request, ct);
                var list = await response.Content.ReadFromJsonAsync<ScimList>(ct)
                    ?? throw new IdentityDirectoryException("WSO2 answered the user search with an empty body.");
                var user = list.Resources?.FirstOrDefault(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
                if (user?.Id is null)
                {
                    return null;
                }

                // Only roles of an application reach that application's tokens; organisation roles such as
                // "everyone" are WSO2's own.
                var roles = (user.Roles ?? [])
                    .Where(r => r.AudienceType == "application" && r.Display is not null)
                    .Select(r => r.Display!)
                    .ToList();
                return new IdentityAccount(user.Id, userName, roles);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task OpenTotpEnrolmentAsync(string accountId, DateTimeOffset until, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return CallAsync<object?>(
            async (client, token, ct) =>
            {
                var body = new JsonObject
                {
                    ["schemas"] = new JsonArray(PatchOpSchema),
                    ["Operations"] = new JsonArray(new JsonObject
                    {
                        ["op"] = "replace",
                        ["value"] = new JsonObject
                        {
                            [TotpEnrolmentClaim.ScimSchema] = new JsonObject
                            {
                                [TotpEnrolmentClaim.ScimAttribute] = until.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                            },
                        },
                    }),
                };
                using var request = ScimRequest(HttpMethod.Patch, $"/{Uri.EscapeDataString(accountId)}", token);
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, ScimJson);
                using var response = await SendAsync(client, request, ct);
                return null;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _tokenLock.Dispose();

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new IdentityDirectoryException(
                string.Create(CultureInfo.InvariantCulture, $"WSO2 answered {request.Method} {request.RequestUri?.AbsolutePath} with {status}."));
        }

        return response;
    }

    private async Task<T> CallAsync<T>(Func<HttpClient, string, CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        var settings = provisioner.Value;
        if (!settings.IsConfigured)
        {
            throw new IdentityDirectoryException($"The provisioner client is not configured ({Wso2ProvisionerOptions.SectionName}).");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        using var client = httpClientFactory.CreateClient(Wso2Backchannel.HttpClientName);
        try
        {
            var token = await TokenAsync(client, settings, timeout.Token);
            return await call(client, token, timeout.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IdentityDirectoryException($"WSO2 did not answer within {settings.TimeoutSeconds} s.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            throw new IdentityDirectoryException("WSO2 could not be reached or answered something unexpected.", ex);
        }
    }

    private async Task<string> TokenAsync(HttpClient client, Wso2ProvisionerOptions settings, CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && timeProvider.GetUtcNow() < _tokenExpires - RenewBefore)
            {
                return _token;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, wso2.Value.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(
                [
                    new KeyValuePair<string, string>("grant_type", "client_credentials"),
                    new KeyValuePair<string, string>("scope", Scopes),
                ]),
            };

            // RFC 6749 §2.3.1: both parts form-encoded before Base64.
            var credentials = $"{Uri.EscapeDataString(settings.ClientId!)}:{Uri.EscapeDataString(settings.ClientSecret!)}";
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
            using var response = await SendAsync(client, request, cancellationToken);
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                throw new IdentityDirectoryException("WSO2 issued no access token to the provisioner.");
            }

            _token = token.AccessToken;
            _tokenExpires = timeProvider.GetUtcNow().AddSeconds(Math.Max(token.ExpiresIn, 0));
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // Addressed to the public authority like every WSO2 call; the back channel's handler rewrites it to the internal one.
    private HttpRequestMessage ScimRequest(HttpMethod method, string suffix, string token)
    {
        var request = new HttpRequestMessage(method, new Uri(wso2.Value.ScimUsersEndpoint + suffix));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ScimJson));
        return request;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record ScimList([property: JsonPropertyName("Resources")] IReadOnlyList<ScimUser>? Resources);

    private sealed record ScimUser(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("userName")] string? UserName,
        [property: JsonPropertyName("roles")] IReadOnlyList<ScimRole>? Roles);

    private sealed record ScimRole(
        [property: JsonPropertyName("display")] string? Display,
        [property: JsonPropertyName("audienceType")] string? AudienceType);
}
