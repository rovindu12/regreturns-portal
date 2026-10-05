using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Auditing;
using RegReturns.Application.Returns;
using RegReturns.Infrastructure.Auditing;
using RegReturns.Infrastructure.Files;
using RegReturns.Infrastructure.Identity.Wso2;
using RegReturns.Infrastructure.Persistence;

namespace RegReturns.Infrastructure;

/// <summary>Registers infrastructure services.</summary>
public static class DependencyInjection
{
    /// <summary>Health check tag for checks that must pass before the app accepts traffic.</summary>
    public const string ReadyTag = "ready";

    /// <summary>Adds the database context, initializer, clock, return file reader and writer, and database health check.</summary>
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

                // Aggregates load several collections (a template's fields and rules); one query each avoids a
                // cartesian product. The Application layer cannot ask per query, as it only references EF Core.
                sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<RegReturnsDbContext>());
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IReturnFileReader, ReturnFileReader>();
        services.TryAddSingleton<IReturnFileWriter, ReturnFileWriter>();

        services.AddHealthChecks()
            .AddDbContextCheck<RegReturnsDbContext>("database", tags: [ReadyTag]);

        return services;
    }

    /// <summary>
    /// Adds the tamper-evident audit trail and the de-duplicating <see cref="AccessDeniedAuditor"/>. Requires <c>Audit:HmacKey</c>; the app refuses to start without it.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddAuditTrail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuditOptions>()
            .Bind(configuration.GetSection(AuditOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddSingleton<AuditHasher>();
        services.TryAddScoped<IAuditTrail, AuditTrail>();
        services.AddMemoryCache();
        services.TryAddScoped<AccessDeniedAuditor>();
        return services;
    }

    /// <summary>
    /// Adds the WSO2 back channel: options, the named <see cref="HttpClient"/> that trusts only the configured CA
    /// and rewrites public WSO2 URLs to the internal address, and a readiness check on discovery and JWKS.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddWso2Backchannel(this IServiceCollection services, IConfiguration configuration)
    {
        // No ValidateDataAnnotations: it reads every property, and the derived addresses throw while Authority is
        // missing, which would hide the message below behind a TargetInvocationException.
        services.AddOptions<Wso2Options>()
            .Bind(configuration.GetSection(Wso2Options.SectionName))
            .Validate(
                o => o.Authority is { IsAbsoluteUri: true, Scheme: "https" },
                $"{Wso2Options.SectionName}:{nameof(Wso2Options.Authority)} must be an absolute https URL.")
            .Validate(
                o => string.IsNullOrWhiteSpace(o.TrustedCaPath) || File.Exists(o.TrustedCaPath),
                $"{Wso2Options.SectionName}:{nameof(Wso2Options.TrustedCaPath)} points to a file that does not exist.")
            .ValidateOnStart();

        services.AddHttpClient(Wso2Backchannel.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(sp =>
                Wso2Backchannel.CreatePrimaryHandler(sp.GetRequiredService<IOptions<Wso2Options>>().Value))
            .AddHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<IOptions<Wso2Options>>().Value;
                return new Wso2BackchannelRewriteHandler(options.Authority!, options.EffectiveBackchannelAuthority);
            });

        services.AddHealthChecks().AddCheck<Wso2HealthCheck>("wso2", tags: [ReadyTag]);
        return services;
    }
}
