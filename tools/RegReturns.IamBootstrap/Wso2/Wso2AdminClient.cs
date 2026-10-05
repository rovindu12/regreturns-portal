using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging;

namespace RegReturns.IamBootstrap.Wso2;

/// <summary>
/// Thin JSON client for WSO2's management (<c>/api/server/v1</c>) and SCIM 2 (<c>/scim2</c>) APIs, authenticated
/// as the super admin. Bodies are never logged: they carry passwords and client secrets.
/// </summary>
/// <param name="http">An HttpClient whose base address is the WSO2 authority and that trusts the dev CA.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class Wso2AdminClient(HttpClient http, ILogger<Wso2AdminClient> logger)
{
    /// <summary>JSON media type for management API bodies.</summary>
    public const string Json = "application/json";

    /// <summary>SCIM media type for <c>/scim2</c> bodies.</summary>
    public const string ScimJson = "application/scim+json";

    /// <summary>Builds the basic credentials header of the super admin.</summary>
    /// <param name="userName">The super admin user name.</param>
    /// <param name="password">The super admin password.</param>
    /// <returns>The header value.</returns>
    public static AuthenticationHeaderValue BasicCredentials(string userName, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}")));

    /// <summary>GETs a resource, returning <see langword="null"/> on 404.</summary>
    /// <param name="path">The path and query, relative to the authority.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The JSON body, or <see langword="null"/> if the resource does not exist.</returns>
    public async Task<JsonNode?> GetOrDefaultAsync(string path, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Get, path, null, Json, cancellationToken);
        response.EnsureSuccess(HttpStatusCode.NotFound);
        return response.StatusCode == HttpStatusCode.NotFound ? null : response.Body;
    }

    /// <summary>GETs a resource that must exist.</summary>
    /// <param name="path">The path and query, relative to the authority.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The JSON body.</returns>
    public async Task<JsonNode> GetAsync(string path, CancellationToken cancellationToken)
    {
        var response = (await SendAsync(HttpMethod.Get, path, null, Json, cancellationToken)).EnsureSuccess();
        return response.Body ?? throw new Wso2ApiException(response);
    }

    /// <summary>Sends a request with an optional JSON body.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path and query, relative to the authority.</param>
    /// <param name="body">The body, if any.</param>
    /// <param name="contentType"><see cref="Json"/> or <see cref="ScimJson"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response; callers decide which statuses are acceptable.</returns>
    public async Task<Wso2Response> SendAsync(
        HttpMethod method, string path, JsonNode? body, string contentType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(method);
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(contentType));
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, contentType);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var displayPath = path.Split('?', 2)[0];
        LogCall(logger, method.Method, displayPath, (int)response.StatusCode);

        JsonNode? json = null;
        if (!string.IsNullOrWhiteSpace(text) && IsJson(response.Content.Headers.ContentType?.MediaType))
        {
            json = JsonNode.Parse(text);
        }

        return new Wso2Response(method.Method, displayPath, response.StatusCode, json, response.Headers.Location);
    }

    private static bool IsJson(string? mediaType) =>
        mediaType is not null && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Debug, Message = "WSO2 {Method} {Path} -> {Status}")]
    private static partial void LogCall(ILogger logger, string method, string path, int status);
}
