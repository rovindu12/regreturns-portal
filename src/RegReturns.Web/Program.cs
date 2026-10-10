using System.Globalization;

using Microsoft.AspNetCore.Mvc;

using RegReturns.Application;
using RegReturns.Application.Abstractions;
using RegReturns.Infrastructure;
using RegReturns.Infrastructure.Identity.Authorization;
using RegReturns.ServiceDefaults.Web;
using RegReturns.Web.Identity;
using RegReturns.Web.Security;
using RegReturns.Web.Status;

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
    builder.AddServiceDefaults("regreturns-web");

    builder.Services.AddApplication();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddAuditTrail(builder.Configuration);
    builder.Services.AddReporting(builder.Configuration);
    builder.Services.AddInsights(builder.Configuration);
    builder.Services.AddDemo(builder.Configuration);
    builder.Services.AddPortalStatus(builder.Configuration);
    builder.Services.AddScoped<PortalDiagnostics>();
    builder.Services.AddWso2Backchannel(builder.Configuration);
    builder.Services.AddIdentityDirectory(builder.Configuration);
    builder.Services.AddRegReturnsAuthorization(builder.Configuration);
    builder.Services.AddPortalAuthentication(builder.Configuration);
    builder.Services.AddPortalDataProtection(builder.Configuration);
    builder.Services.AddAntiforgery(options =>
    {
        options.Cookie.Name = PortalSession.AntiforgeryCookieName;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });
    builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
    builder.Services.AddHsts(options =>
    {
        options.MaxAge = TimeSpan.FromDays(365);
        options.IncludeSubDomains = true;
    });

    var app = builder.Build();

    app.UseServiceDefaults();
    app.UsePortalSecurityHeaders();
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    // A browser asking for a page that is not there gets a page in the portal's layout rather than an empty answer.
    // GET and HEAD only: re-executing a POST would run the anti-forgery check again on the error page.
    app.UseWhen(
        context => HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method),
        branch => branch.UseStatusCodePagesWithReExecute("/Home/HttpError", "?code={0}"));

    app.UseHttpsRedirectionUnlessBehindProxy();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapDefaultEndpoints();
    app.MapStaticAssets().AllowAnonymous();
    app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}")
        .WithStaticAssets();

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "RegReturns web portal terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Entry point; public so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program
{
    /// <summary>Prevents instantiation outside the generated entry point.</summary>
    protected Program()
    {
    }
}
