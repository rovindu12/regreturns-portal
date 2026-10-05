using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;

using Asp.Versioning;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using RegReturns.Api.Authentication;
using RegReturns.Api.OpenApi;
using RegReturns.Api.Problems;
using RegReturns.Api.RateLimiting;
using RegReturns.Application.Identity;
using RegReturns.Infrastructure;

namespace RegReturns.Api.Hosting;

/// <summary>Registers and maps the API's own plumbing: versions, JSON, problems, OpenAPI, idempotency and rate limits.</summary>
internal static class ApiHostingExtensions
{
    /// <summary>The first and current API version.</summary>
    public static readonly ApiVersion V1 = new(1);

    /// <summary>
    /// Adds controllers with the API's JSON and problem conventions, URL-segment versioning, the OpenAPI document,
    /// idempotency, rate limiting, and the client actor that runs use cases as the calling client's client user.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddRegReturnsApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIdempotency(configuration);
        services.AddScoped<ICurrentClient, HttpCurrentClient>();
        services.Replace(ServiceDescriptor.Scoped<ICurrentActor, ClientActor>());

        services.AddControllers()
            .AddJsonOptions(options => ConfigureJson(options.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ApiProblems.InvalidRequest);
        services.ConfigureHttpJsonOptions(options => ConfigureJson(options.SerializerOptions));

        services.AddApiVersioning(options =>
            {
                options.DefaultApiVersion = V1;
                options.ReportApiVersions = true;
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddMvc()
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            })
            .AddOpenApi(options =>
            {
                options.Document.AddDocumentTransformer<ApiDocumentTransformer>();
                options.Document.AddOperationTransformer<ApiDocumentTransformer>();
            });

        services.AddOptions<ApiDocumentationOptions>().Bind(configuration.GetSection(ApiDocumentationOptions.SectionName));
        services.AddApiRateLimiting(configuration);
        return services;
    }

    /// <summary>
    /// Serves Swagger UI at <c>/swagger</c>, signing in with client credentials as the configured demo client.
    /// Call before authentication: the UI's static files need no token.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application.</returns>
    public static WebApplication UseApiDocumentation(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var settings = app.Services.GetRequiredService<IOptions<ApiDocumentationOptions>>().Value;
        if (!settings.Enabled)
        {
            return app;
        }

        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "RegReturns API";
            options.SwaggerEndpoint($"/openapi/v{V1.MajorVersion}.json", "RegReturns API v1");
            options.OAuthAppName("RegReturns API");
            options.OAuthScopes(ApiScopes.ReturnsRead, ApiScopes.ReferenceRead);
            if (!string.IsNullOrWhiteSpace(settings.ClientId))
            {
                options.OAuthClientId(settings.ClientId);
            }
        });
        return app;
    }

    /// <summary>Maps the OpenAPI document (one per version, public) and the controllers (rate limited).</summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application.</returns>
    public static WebApplication MapRegReturnsApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapOpenApi().WithDocumentPerVersion().AllowAnonymous();
        app.MapControllers().RequireRateLimiting(ApiRateLimiting.PolicyName);
        return app;
    }

    private static void ConfigureJson(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new JsonStringEnumConverter());
    }
}
