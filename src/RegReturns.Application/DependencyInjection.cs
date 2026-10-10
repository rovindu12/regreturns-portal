using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using RegReturns.Application.Demo;
using RegReturns.Application.Diagnostics;
using RegReturns.Application.Identity;
using RegReturns.Application.Insights;
using RegReturns.Application.Messaging;
using RegReturns.Application.Reporting;
using RegReturns.Application.Returns;

namespace RegReturns.Application;

/// <summary>Registers application use cases.</summary>
public static class DependencyInjection
{
    /// <summary>Registers every query and command handler in this assembly, and the services they share, as scoped.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var handlerInterfaces = new[] { typeof(IQueryHandler<,>), typeof(ICommandHandler<,>) };
        var handlers = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && handlerInterfaces.Contains(i.GetGenericTypeDefinition()))
                .Select(i => (Service: i, Implementation: t)));

        foreach (var (service, implementation) in handlers)
        {
            services.AddScoped(service, implementation);
        }

        services.AddScoped<ICurrentActor, CurrentActor>();
        services.AddScoped<ReturnValidator>();
        services.AddScoped<ReportBuilder>();

        // Only the portal draws reports (AddReporting in Infrastructure); elsewhere the report handlers refuse.
        services.AddOptions<ReportingOptions>();
        services.TryAddSingleton<IReportingReadModel, UnavailableReportingReadModel>();
        services.TryAddSingleton<IComplianceReportRenderer, UnavailableComplianceReportRenderer>();

        // Insights are rule-based unless the host adds an AI provider (AddInsights in Infrastructure).
        services.TryAddSingleton<IInsightNarrator, RuleBasedNarrator>();

        // Only the portal resets the demo (AddDemo in Infrastructure); other hosts get a reset that always refuses.
        services.AddOptions<DemoOptions>();
        services.TryAddSingleton<IDemoResetSchedule, NoDemoResetSchedule>();
        services.TryAddScoped<IDemoReset, UnavailableDemoReset>();

        // Only the portal reaches WSO2's SCIM API (AddIdentityDirectory in Infrastructure); elsewhere every call fails.
        services.AddOptions<TotpEnrolmentOptions>();
        services.TryAddSingleton<IIdentityDirectory, UnavailableIdentityDirectory>();
        services.TryAddScoped<IDatabaseDiagnostics, UnavailableDatabaseDiagnostics>();

        return services;
    }
}
