using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

using RegReturns.Api.Idempotency;
using RegReturns.Application.Authorization;
using RegReturns.Application.Identity;
using RegReturns.Infrastructure.Identity.Wso2;

namespace RegReturns.Api.OpenApi;

/// <summary>Settings of the API documentation (configuration section <c>Api:Swagger</c>, ADR 0027).</summary>
public sealed class ApiDocumentationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Api:Swagger";

    /// <summary>Gets or sets a value indicating whether Swagger UI is served at <c>/swagger</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the client id Swagger UI fills in when signing in: the public, read-only demo client
    /// (<c>DEMO_API_CLIENT_ID</c> in <c>.env.generated</c>). Its secret is never configured here.
    /// </summary>
    public string? ClientId { get; set; }
}

/// <summary>
/// Completes the generated OpenAPI document (ADR 0027): a title and an overview of the conventions, the OAuth 2.0
/// client-credentials scheme against WSO2's token endpoint, the scopes each operation needs (from its policy), and the
/// <c>Idempotency-Key</c> header of idempotent operations.
/// </summary>
/// <param name="wso2">The WSO2 settings, for the token endpoint.</param>
internal sealed class ApiDocumentTransformer(IOptions<Wso2Options> wso2) : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    /// <summary>The name of the security scheme.</summary>
    public const string SchemeName = "oauth2";

    /// <summary>The scope each API policy requires.</summary>
    public static readonly IReadOnlyDictionary<string, string> PolicyScopes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Policies.ApiReturnsRead] = ApiScopes.ReturnsRead,
        [Policies.ApiReturnsSubmit] = ApiScopes.ReturnsSubmit,
        [Policies.ApiReferenceRead] = ApiScopes.ReferenceRead,
    };

    private const string Overview = """
        Bank systems read reference data and their bank's returns, and deliver returns as drafts that a bank checker
        submits in the portal. Every operation needs a client-credentials access token from the Bank of Valoria's
        identity server; a client sees only its own bank's data, and another bank's ids answer 404.

        - **Errors** are RFC 9457 problem details with a `traceId` and, when a rule refused the request, a stable `code`.
        - **Lists** are paged with `page` (from 1) and `pageSize` (up to 100); the `Link` header has first, prev, next and last.
        - **POST** requests need an `Idempotency-Key` header. A retry with the same key and body gets the first response
          again with `Idempotent-Replayed: true`; the same key with another body is 422.
        - **Rate limit**: a fixed window per client; a refusal is 429 with `Retry-After`.
        """;

    /// <inheritdoc />
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Info.Title = "RegReturns API";
        document.Info.Description = Overview;
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Description = "Client credentials issued by the Bank of Valoria's identity server (WSO2).",
            Flows = new OpenApiOAuthFlows
            {
                ClientCredentials = new OpenApiOAuthFlow
                {
                    TokenUrl = wso2.Value.TokenEndpoint,
                    Scopes = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [ApiScopes.ReturnsRead] = "Read your bank's returns.",
                        [ApiScopes.ReturnsSubmit] = "Deliver returns for your bank, as drafts.",
                        [ApiScopes.ReferenceRead] = "Read reference data: institutions and return types.",
                    },
                },
            },
        };
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var scopes = metadata.OfType<IAuthorizeData>()
            .Select(data => data.Policy is { } policy ? PolicyScopes.GetValueOrDefault(policy) : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (scopes.Count > 0)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = scopes,
            });
        }

        if (metadata.OfType<IdempotentAttribute>().Any())
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = IdempotentAttribute.KeyHeader,
                In = ParameterLocation.Header,
                Required = true,
                Description = "A value unique to this request, such as a UUID (1 to 255 visible ASCII characters). Send the same value when you retry.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, MinLength = 1, MaxLength = 255 },
            });
        }

        return Task.CompletedTask;
    }
}
