using System.Globalization;

using Microsoft.AspNetCore.Mvc;

using RegReturns.Application;
using RegReturns.Application.Abstractions;
using RegReturns.Infrastructure;
using RegReturns.Infrastructure.Identity.Authorization;
using RegReturns.ServiceDefaults.Web;
using RegReturns.Web.Identity;
using RegReturns.Web.Status;

using Serilog;

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
    builder.Services.AddSingleton<PortalStatus>();
    builder.Services.AddWso2Backchannel(builder.Configuration);
    builder.Services.AddRegReturnsAuthorization(builder.Configuration);
    builder.Services.AddPortalAuthentication(builder.Configuration);
    builder.Services.AddAntiforgery(options =>
    {
        options.Cookie.Name = PortalSession.AntiforgeryCookieName;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });
    builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

    var app = builder.Build();

    app.UseServiceDefaults();
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
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
