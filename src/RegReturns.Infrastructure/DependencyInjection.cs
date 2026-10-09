using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Auditing;
using RegReturns.Application.Demo;
using RegReturns.Application.Diagnostics;
using RegReturns.Application.Idempotency;
using RegReturns.Application.Identity;
using RegReturns.Application.Insights;
using RegReturns.Application.Migration;
using RegReturns.Application.Reporting;
using RegReturns.Application.Returns;
using RegReturns.Domain.Insights;
using RegReturns.Infrastructure.Ai;
using RegReturns.Infrastructure.Auditing;
using RegReturns.Infrastructure.Demo;
using RegReturns.Infrastructure.Files;
using RegReturns.Infrastructure.Idempotency;
using RegReturns.Infrastructure.Identity.Wso2;
using RegReturns.Infrastructure.Legacy;
using RegReturns.Infrastructure.Persistence;
using RegReturns.Infrastructure.Reporting;

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
        services.Replace(ServiceDescriptor.Scoped<IDatabaseDiagnostics, DatabaseDiagnosticsReader>());
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IReturnFileReader, ReturnFileReader>();
        services.TryAddSingleton<IReturnFileWriter, ReturnFileWriter>();

        services.AddHealthChecks()
            .AddDbContextCheck<RegReturnsDbContext>("database", tags: [ReadyTag]);

        return services;
    }

    /// <summary>
    /// Adds the tamper-evident audit trail, the de-duplicating <see cref="AccessDeniedAuditor"/>, the chain verifier and
    /// the data-change auditor, which makes the <see cref="RegReturnsDbContext"/> record every save in the chain
    /// (ADR 0024). Requires <c>Audit:HmacKey</c>, which the app refuses to start without, and an
    /// <see cref="IAuditContext"/> from the host saying who is acting. The migrator does not call this, so seeding
    /// writes no audit entries.
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
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<AuditHasher>();
        services.TryAddScoped<IAuditTrail, AuditTrail>();
        services.TryAddScoped<IAuditChainVerifier, AuditChainVerifier>();
        services.TryAddScoped<IDataChangeAuditor, DataChangeAuditor>();
        services.AddMemoryCache();
        services.TryAddScoped<AccessDeniedAuditor>();
        return services;
    }

    /// <summary>
    /// Adds the idempotency store for API requests with an <c>Idempotency-Key</c> and the background purge of expired
    /// records (ADR 0027), configured by <c>Api:Idempotency</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddIdempotency(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdempotencyOptions>()
            .Bind(configuration.GetSection(IdempotencyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddHostedService<IdempotencyPurger>();
        return services;
    }

    /// <summary>
    /// Adds the dashboards' read model over the <c>reporting</c> views and the report renderer (ADR 0028), configured
    /// by <c>Reports</c>. Requires <see cref="AddInfrastructure"/> for the database settings.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddReporting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ReportingOptions>()
            .Bind(configuration.GetSection(ReportingOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => o.KeyRatios.All(k => !string.IsNullOrWhiteSpace(k.ReturnType) && !string.IsNullOrWhiteSpace(k.Field)),
                $"Every {ReportingOptions.SectionName}:{nameof(ReportingOptions.KeyRatios)} entry needs a ReturnType and a Field.")
            .ValidateOnStart();
        services.TryAddSingleton<IReportingReadModel, ReportingReadModel>();
        services.TryAddSingleton<IComplianceReportRenderer, ComplianceReportRenderer>();
        return services;
    }

    /// <summary>
    /// Adds advisory insights (ADR 0030), configured by <c>Ai</c>: the Anthropic narrator and its named
    /// <see cref="HttpClient"/> when <c>Ai:Provider</c> is <c>Anthropic</c> (the default), the rule-based writer
    /// otherwise. Without <c>Ai:Anthropic:ApiKey</c> every insight is rule-based and nothing is sent.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddInsights(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);

        // The narrator's deadline (Ai:Anthropic:TimeoutSeconds) bounds every call, so HttpClient's own 100 s limit is off.
        services.AddHttpClient(AnthropicInsightNarrator.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.TryAddSingleton<AnthropicInsightNarrator>();
        services.AddSingleton<IInsightNarrator>(sp =>
            sp.GetRequiredService<IOptions<AiOptions>>().Value.Provider == InsightProvider.Anthropic
                ? sp.GetRequiredService<AnthropicInsightNarrator>()
                : new RuleBasedNarrator());
        return services;
    }

    /// <summary>
    /// Adds the legacy migration (ADR 0029): the migrator and its CSV report writer. The host also adds the audit trail
    /// and an <see cref="IAuditContext"/>, so every migrated return is recorded in the audit chain.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddLegacyMigration(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ILegacyMigrator, LegacyMigrator>();
        services.TryAddSingleton<IMigrationReportWriter, MigrationReportWriter>();
        return services;
    }

    /// <summary>
    /// Adds the public demo (ADR 0031), configured by <c>Demo</c>: the reset (one transaction that replaces the workload
    /// and records a <c>DemoReset</c> audit event), its cron schedule and the job that runs it. Requires
    /// <see cref="AddInfrastructure"/> and <see cref="AddAuditTrail"/>. The job does nothing unless <c>Demo:Enabled</c>
    /// is on and <c>Demo:ResetSchedule</c> is set.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddDemo(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DemoOptions>()
            .Bind(configuration.GetSection(DemoOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => CronDemoResetSchedule.IsValid(o.ResetSchedule),
                $"{DemoOptions.SectionName}:{nameof(DemoOptions.ResetSchedule)} must be empty or a five-field cron expression (UTC), such as 0 3 * * *.")
            .Validate(
                o => o.ApiBaseUrl is null || o.ApiBaseUrl is { IsAbsoluteUri: true, Scheme: "https" },
                $"{DemoOptions.SectionName}:{nameof(DemoOptions.ApiBaseUrl)} must be an absolute https URL.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.Replace(ServiceDescriptor.Singleton<IDemoResetSchedule, CronDemoResetSchedule>());
        services.Replace(ServiceDescriptor.Scoped<IDemoReset, DemoResetService>());
        services.AddHostedService<DemoResetJob>();
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
                // Read at start, so a missing or unreadable file (a file mode on the server) stops the host at once
                // instead of failing every sign-in and health check behind a cached options error.
                o => string.IsNullOrWhiteSpace(o.TrustedCaPath) || Wso2CertificateValidator.CanLoad(o.TrustedCaPath),
                $"{Wso2Options.SectionName}:{nameof(Wso2Options.TrustedCaPath)} must name a readable PEM file with at least one certificate.")
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

    /// <summary>
    /// Adds WSO2 as the identity directory (ADR 0032): SCIM 2 calls with the provisioner client
    /// (<c>Iam:Provisioner</c>) over the back channel, and the length of TOTP enrolment windows
    /// (<c>Iam:TotpEnrolment</c>). Requires <see cref="AddWso2Backchannel"/>. Without provisioner credentials the
    /// directory answers every call with an error, so administrators see that WSO2 cannot be changed.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddIdentityDirectory(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<Wso2ProvisionerOptions>()
            .Bind(configuration.GetSection(Wso2ProvisionerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<TotpEnrolmentOptions>()
            .Bind(configuration.GetSection(TotpEnrolmentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.Replace(ServiceDescriptor.Singleton<IIdentityDirectory, Wso2IdentityDirectory>());
        return services;
    }
}
