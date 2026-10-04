using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using RegReturns.Application.Abstractions;
using RegReturns.Infrastructure.Persistence;

namespace RegReturns.Infrastructure;

/// <summary>Registers infrastructure services.</summary>
public static class DependencyInjection
{
    /// <summary>Health check tag for checks that must pass before the app accepts traffic.</summary>
    public const string ReadyTag = "ready";

    /// <summary>Adds the database context, initializer, clock and database health check.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection("Database"))
            .Configure(o => o.ConnectionString =
                configuration.GetConnectionString(DatabaseOptions.ConnectionStringName) ?? string.Empty)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<RegReturnsDbContext>((sp, options) =>
        {
            var db = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            options.UseSqlServer(db.ConnectionString, sql =>
            {
                sql.CommandTimeout(db.CommandTimeoutSeconds);
                sql.EnableRetryOnFailure(db.MaxRetryCount);
            });
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<RegReturnsDbContext>());
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddHealthChecks()
            .AddDbContextCheck<RegReturnsDbContext>("database", tags: [ReadyTag]);

        return services;
    }
}
