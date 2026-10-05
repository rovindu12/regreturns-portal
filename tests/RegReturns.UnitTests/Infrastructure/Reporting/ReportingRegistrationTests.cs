using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using RegReturns.Application.Reporting;
using RegReturns.Infrastructure;
using RegReturns.Infrastructure.Reporting;

namespace RegReturns.UnitTests.Infrastructure.Reporting;

public sealed class ReportingRegistrationTests
{
    [Theory]
    [InlineData(typeof(IReportingReadModel), typeof(ReportingReadModel))]
    [InlineData(typeof(IComplianceReportRenderer), typeof(ComplianceReportRenderer))]
    public void Reporting_services_are_singletons(Type service, Type implementation)
    {
        var services = new ServiceCollection().AddReporting(Configuration([]));

        var descriptor = services.Where(d => d.ServiceType == service).ShouldHaveSingleItem();
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        descriptor.ImplementationType.ShouldBe(implementation);
    }

    [Fact]
    public void Key_ratios_bind_from_configuration()
    {
        var options = Options(new()
        {
            ["Reports:MonthsShown"] = "6",
            ["Reports:KeyRatios:0:ReturnType"] = "MLR",
            ["Reports:KeyRatios:0:Field"] = "LCR",
        });

        options.MonthsShown.ShouldBe(6);
        options.KeyRatios.ShouldHaveSingleItem().Field.ShouldBe("LCR");
    }

    [Fact]
    public void A_key_ratio_without_a_field_is_refused()
    {
        Should.Throw<OptionsValidationException>(() => Options(new() { ["Reports:KeyRatios:0:ReturnType"] = "MLR" }))
            .Message.ShouldContain("needs a ReturnType and a Field");
    }

    [Fact]
    public void Too_few_months_are_refused()
    {
        Should.Throw<OptionsValidationException>(() => Options(new() { ["Reports:MonthsShown"] = "1" }));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ReportingOptions Options(Dictionary<string, string?> values)
    {
        using var provider = new ServiceCollection().AddReporting(Configuration(values)).BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ReportingOptions>>().Value;
    }
}
