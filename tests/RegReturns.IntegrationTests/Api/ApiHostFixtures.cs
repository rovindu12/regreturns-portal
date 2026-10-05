extern alias ApiHost;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Api;

/// <summary>
/// The API host on its own seeded database, authenticated by the header-driven <see cref="TestAuth"/> scheme.
/// Shared by the tests of one class; they register their own clients so they never interfere.
/// </summary>
public sealed class TestSchemeApiFixture(SqlServerFixture sql) : IAsyncLifetime
{
    /// <summary>Gets the database.</summary>
    public ApiDatabase Database { get; private set; } = null!;

    /// <summary>Gets the host factory.</summary>
    public WebApplicationFactory<ApiHost::Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Database = await ApiDatabase.CreateAsync(sql);
        Factory = ApiHostFixtures.CreateFactory(Database, builder => builder.UseTestAuth());
    }

    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}

/// <summary>
/// The API host on its own seeded database with its real JWT bearer configuration, except that signing keys come
/// from <see cref="ApiTokens.Configuration"/> instead of WSO2's discovery document and JWKS.
/// </summary>
public sealed class BearerApiFixture(SqlServerFixture sql) : IAsyncLifetime
{
    /// <summary>Gets the database.</summary>
    public ApiDatabase Database { get; private set; } = null!;

    /// <summary>Gets the host factory.</summary>
    public WebApplicationFactory<ApiHost::Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Database = await ApiDatabase.CreateAsync(sql);
        Factory = ApiHostFixtures.CreateFactory(Database, builder => builder
            .UseSetting("Wso2:Authority", ApiTokens.Authority)
            .UseSetting("Audit:HmacKey", TestAuth.AuditKey)
            .ConfigureTestServices(services =>
            {
                // Runs after the API's own configuration; JwtBearerPostConfigureOptions then builds a static
                // configuration manager from it instead of fetching discovery and JWKS from WSO2.
                services.Configure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme, options => options.Configuration = ApiTokens.Configuration);
                services.PostConfigure<HealthCheckServiceOptions>(options =>
                {
                    foreach (var check in options.Registrations.Where(r => r.Name == "wso2").ToList())
                    {
                        options.Registrations.Remove(check);
                    }
                });
            }));
    }

    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}

/// <summary>
/// The test-scheme API host with a rate limit of <see cref="PermitLimit"/> requests an hour per client, so tests reach
/// it quickly and the window never resets under them.
/// </summary>
public sealed class RateLimitedApiFixture(SqlServerFixture sql) : IAsyncLifetime
{
    /// <summary>The requests a client may make in the window.</summary>
    public const int PermitLimit = 2;

    /// <summary>Gets the database.</summary>
    public ApiDatabase Database { get; private set; } = null!;

    /// <summary>Gets the host factory.</summary>
    public WebApplicationFactory<ApiHost::Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Database = await ApiDatabase.CreateAsync(sql);
        Factory = ApiHostFixtures.CreateFactory(Database, builder => builder
            .UseTestAuth()
            .UseSetting("Api:RateLimit:PermitLimit", PermitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .UseSetting("Api:RateLimit:WindowSeconds", "3600"));
    }

    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}

internal static class ApiHostFixtures
{
    public static WebApplicationFactory<ApiHost::Program> CreateFactory(ApiDatabase database, Action<IWebHostBuilder> configure) =>
        new WebApplicationFactory<ApiHost::Program>().WithWebHostBuilder(builder =>
        {
            builder
                .UseEnvironment("Testing")
                .UseSetting("Serilog:MinimumLevel:Default", "Warning")
                .UseSetting("ConnectionStrings:RegReturns", database.ConnectionString);
            configure(builder);
        });
}
