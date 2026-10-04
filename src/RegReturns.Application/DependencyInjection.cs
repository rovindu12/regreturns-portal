using Microsoft.Extensions.DependencyInjection;

using RegReturns.Application.Messaging;

namespace RegReturns.Application;

/// <summary>Registers application use cases.</summary>
public static class DependencyInjection
{
    /// <summary>Registers every query and command handler in this assembly as a scoped service.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var handlerInterfaces = new[] { typeof(IQueryHandler<,>) };
        var handlers = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && handlerInterfaces.Contains(i.GetGenericTypeDefinition()))
                .Select(i => (Service: i, Implementation: t)));

        foreach (var (service, implementation) in handlers)
        {
            services.AddScoped(service, implementation);
        }

        return services;
    }
}
