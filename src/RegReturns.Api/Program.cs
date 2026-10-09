using System.Globalization;

using RegReturns.Api.Authentication;
using RegReturns.Api.Hosting;
using RegReturns.Application;
using RegReturns.Infrastructure;
using RegReturns.ServiceDefaults.Web;

using Serilog;

// The container's HEALTHCHECK runs the app's own binary (ADR 0034): answer it before building anything.
if (HealthProbe.IsRequested(args))
{
    return await HealthProbe.RunAsync(args);
}

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.AddServiceDefaults("regreturns-api");

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApiAuthentication(builder.Configuration);
    builder.Services.AddRegReturnsApi(builder.Configuration);
    builder.Services.AddHsts(options =>
    {
        options.MaxAge = TimeSpan.FromDays(365);
        options.IncludeSubDomains = true;
    });

    var app = builder.Build();

    app.UseServiceDefaults();
    app.UseApiSecurityHeaders();
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseHttpsRedirectionUnlessBehindProxy();
    app.UseApiDocumentation();
    app.UseAuthentication();

    // After authentication, so each client has its own window; before authorization, so refused calls count too.
    app.UseRateLimiter();
    app.UseAuthorization();

    app.MapDefaultEndpoints();
    app.MapRegReturnsApi();

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "RegReturns API terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Entry point; public so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program
{
    /// <summary>Prevents instantiation outside the generated entry point.</summary>
    protected Program()
    {
    }
}
