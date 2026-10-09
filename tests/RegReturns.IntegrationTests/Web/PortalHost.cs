using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Web;

/// <summary>Builds the portal host for tests: test authentication, a given database and optional service overrides.</summary>
internal static class PortalHost
{
    /// <summary>The portal only issues Secure cookies, so clients talk to the test server over HTTPS.</summary>
    public static readonly Uri BaseAddress = new("https://localhost");

    public static WebApplicationFactory<Program> Create(
        string connectionString,
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string?>? settings = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder
                .UseEnvironment("Testing")
                .UseSetting("Serilog:MinimumLevel:Default", "Warning")
                .UseSetting("ConnectionStrings:RegReturns", connectionString)

                // Never let the nightly demo reset fire in a test run that crosses 03:00 UTC.
                .UseSetting("Demo:ResetSchedule", string.Empty)
                .UseTestAuth();
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            {
                builder.UseSetting(key, value);
            }

            if (configureServices is not null)
            {
                builder.ConfigureTestServices(configureServices);
            }
        });
}
