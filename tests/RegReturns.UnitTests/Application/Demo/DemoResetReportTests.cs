using RegReturns.Application.Demo;

namespace RegReturns.UnitTests.Application.Demo;

public sealed class DemoResetReportTests
{
    private static readonly DemoTableCount[] Removed =
    [
        new("returns.SubmissionValues", 900),
        new("returns.Submissions", 120),
        new("returns.Obligations", 140),
        new("reference.TemplateVersions", 4),
        new("api.IdempotencyRecords", 0),
    ];

    private static readonly DemoSeedCounts Seeded = new(3, 3, 140, 118, 0, 0);

    [Fact]
    public void Describes_what_a_scheduled_reset_removed_and_seeded()
    {
        DemoResetReport.Describe(DemoResetTrigger.Scheduled, Removed, Seeded, 1234).ShouldBe(
            "Scheduled demo reset; removed 1164 rows (120 returns, 140 obligations, 4 template versions); "
            + "seeded 118 returns, 140 obligations, 3 template versions; directory and audit trail kept; 1234 ms");
    }

    [Fact]
    public void Names_institutions_and_accounts_it_had_to_create()
    {
        var details = DemoResetReport.Describe(DemoResetTrigger.Manual, Removed, Seeded with { Institutions = 1, Users = 2 }, 5);

        details.ShouldStartWith("Manual demo reset; ");
        details.ShouldContain("3 template versions, 1 missing institutions and 2 missing demo accounts; directory");
    }

    [Fact]
    public void Counts_every_row_removed()
    {
        new DemoResetReport(DemoResetTrigger.Manual, DateTimeOffset.UnixEpoch, 5, 42, Removed, Seeded)
            .RowsRemoved.ShouldBe(1164);
    }
}
