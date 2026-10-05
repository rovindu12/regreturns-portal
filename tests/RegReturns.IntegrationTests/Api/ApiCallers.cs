extern alias ApiHost;

using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using RegReturns.Application.Identity;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Api;

/// <summary>Test-scheme callers of the API and helpers to read its answers.</summary>
internal static class ApiCallers
{
    /// <summary>Every scope a bank's own client is granted.</summary>
    public const string BankScopes = $"{ApiScopes.ReturnsRead} {ApiScopes.ReturnsSubmit} {ApiScopes.ReferenceRead}";

    /// <summary>Creates a client that calls as a client-credentials token of the given client and scopes.</summary>
    public static HttpClient ClientAs(this WebApplicationFactory<ApiHost::Program> factory, string clientId, string scope = BankScopes) =>
        factory.CreateClientAs(Claims(clientId, scope));

    /// <summary>The claims of a client-credentials token as WSO2 issues it.</summary>
    public static (string Type, string Value)[] Claims(string clientId, string scope) =>
    [
        (ClaimNames.AuthorizedUserType, ClaimNames.ApplicationTokenType),
        (ClaimNames.Subject, clientId),
        (ClaimNames.AuthorizedParty, clientId),
        (ClaimNames.ClientId, clientId),
        (ClaimNames.Scope, scope),
    ];

    /// <summary>Posts JSON with an idempotency key.</summary>
    public static Task<HttpResponseMessage> PostWithKeyAsync(this HttpClient client, string path, object body, string? key, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return client.SendAsync(request, ct);
    }

    /// <summary>Reads a JSON body as a document.</summary>
    public static async Task<JsonDocument> ReadJsonAsync(this HttpResponseMessage response, CancellationToken ct) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

    /// <summary>Reads the stable error code of a problem response.</summary>
    public static async Task<string?> ReadProblemCodeAsync(this HttpResponseMessage response, CancellationToken ct)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var json = await response.ReadJsonAsync(ct);
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>Returns the W3C trace id the API put on a response.</summary>
    public static string TraceId(this HttpResponseMessage response) => response.Headers.GetValues("X-Trace-Id").Single();
}
