using System.Globalization;

using RegReturns.Application;
using RegReturns.Infrastructure;
using RegReturns.ServiceDefaults.Web;

using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.AddServiceDefaults("regreturns-web");

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddControllersWithViews();

    var app = builder.Build();

    app.UseServiceDefaults();
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Home/Error");
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseRouting();
    app.UseAuthorization();

    app.MapDefaultEndpoints();
    app.MapStaticAssets();
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
