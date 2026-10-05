using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using RegReturns.Application.Returns;
using RegReturns.Infrastructure;
using RegReturns.Infrastructure.Files;

namespace RegReturns.UnitTests.Infrastructure.Files;

public sealed class FileServicesRegistrationTests
{
    [Theory]
    [InlineData(typeof(IReturnFileReader), typeof(ReturnFileReader))]
    [InlineData(typeof(IReturnFileWriter), typeof(ReturnFileWriter))]
    public void Infrastructure_registers_the_file_services_as_singletons(Type service, Type implementation)
    {
        var services = new ServiceCollection().AddInfrastructure(new ConfigurationBuilder().Build());

        var descriptor = services.Where(d => d.ServiceType == service).ShouldHaveSingleItem();
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        descriptor.ImplementationType.ShouldBe(implementation);
    }
}
