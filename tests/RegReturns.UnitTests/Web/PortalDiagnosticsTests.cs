using Microsoft.Extensions.Diagnostics.HealthChecks;

using RegReturns.Web.Status;

namespace RegReturns.UnitTests.Web;

public sealed class PortalDiagnosticsTests
{
    [Fact]
    public void A_failed_check_shows_its_exception_type_and_a_readable_part_of_the_message()
    {
        var entry = new HealthReportEntry(
            HealthStatus.Unhealthy, "WSO2 discovery failed", TimeSpan.FromMilliseconds(12.5), new HttpRequestException(new string('x', 1000)), null, ["ready"]);

        var line = PortalDiagnostics.Line("wso2", entry);

        line.Error.ShouldNotBeNull().ShouldStartWith("HttpRequestException: xxx");
        line.Error.Length.ShouldBe(301);
        line.Tags.ShouldBe("ready");
        line.DurationMs.ShouldBe(12.5);
    }

    [Fact]
    public void A_healthy_check_has_no_error()
    {
        var entry = new HealthReportEntry(HealthStatus.Healthy, null, TimeSpan.Zero, null, null, ["live"]);

        PortalDiagnostics.Line("self", entry).Error.ShouldBeNull();
    }
}
