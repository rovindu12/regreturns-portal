using Microsoft.Extensions.Diagnostics.HealthChecks;

using RegReturns.Web.Models;
using RegReturns.Web.Status;

namespace RegReturns.UnitTests.Web.Demo;

public sealed class PortalStatusTests
{
    private static readonly DateTimeOffset CheckedAt = new(2026, 10, 9, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Lists_the_portal_then_each_check_by_its_public_name()
    {
        var report = PortalStatus.ToReport(Health(("wso2", HealthStatus.Healthy), ("database", HealthStatus.Healthy)), CheckedAt);

        report.Components.Select(c => c.Name).ShouldBe(["Portal", "Database", "Identity server"]);
        report.Overall.ShouldBe(StatusLevel.Operational);
        report.CheckedAt.ShouldBe(CheckedAt);
    }

    [Fact]
    public void Lists_the_api_after_the_portals_own_dependencies()
    {
        var report = PortalStatus.ToReport(
            Health((PortalStatus.ApiCheck, HealthStatus.Unhealthy), ("wso2", HealthStatus.Healthy), ("database", HealthStatus.Healthy)),
            CheckedAt);

        report.Components.Select(c => c.Name).ShouldBe(["Portal", "Database", "Identity server", "REST API"]);
        report.Overall.ShouldBe(StatusLevel.Down);
    }

    [Fact]
    public void The_worst_component_decides_the_overall_level()
    {
        var report = PortalStatus.ToReport(Health(("database", HealthStatus.Healthy), ("wso2", HealthStatus.Unhealthy)), CheckedAt);

        report.Overall.ShouldBe(StatusLevel.Down);
        report.Components.Single(c => c.Name == "Identity server").Level.ShouldBe(StatusLevel.Down);
        new StatusViewModel(report, "1.0.0", new(false, [], null, null, 10)).Headline.ShouldBe("Some systems are down");
    }

    [Fact]
    public void Shows_levels_only_never_a_checks_description_or_error()
    {
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["database"] = new(HealthStatus.Degraded, "Server=sql;Password=secret", TimeSpan.Zero, new InvalidOperationException("boom"), null),
        };

        var report = PortalStatus.ToReport(new HealthReport(entries, TimeSpan.Zero), CheckedAt);

        report.Components.ShouldAllBe(c => !c.Purpose.Contains("Password", StringComparison.Ordinal));
        report.Overall.ShouldBe(StatusLevel.Degraded);
    }

    [Theory]
    [InlineData(StatusLevel.Operational, "Operational", "text-bg-success")]
    [InlineData(StatusLevel.Degraded, "Degraded", "text-bg-warning")]
    [InlineData(StatusLevel.Down, "Down", "text-bg-danger")]
    public void Every_level_is_spelled_out_as_well_as_coloured(StatusLevel level, string label, string badge)
    {
        StatusViewModel.Label(level).ShouldBe(label);
        StatusViewModel.Badge(level).ShouldBe(badge);
    }

    private static HealthReport Health(params (string Name, HealthStatus Status)[] checks) =>
        new(checks.ToDictionary(c => c.Name, c => new HealthReportEntry(c.Status, null, TimeSpan.Zero, null, null)), TimeSpan.Zero);
}
