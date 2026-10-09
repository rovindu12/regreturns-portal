extern alias MigratorTool;

using MigratorTool::RegReturns.Migrator.Legacy;

using RegReturns.Application.Migration;
using RegReturns.Domain.Migration;
using RegReturns.UnitTests.TestSupport;

namespace RegReturns.UnitTests.Migrator;

public sealed class LegacyConsoleReportTests
{
    private const string Excluded = " 1 row(s) rejected and 1 superseded; see row-errors.csv in the report folder.";

    private static readonly MigrationTotals Clean = new(8, 0, 0, 0, 7, 1);

    [Fact]
    public void The_report_names_the_run_and_whether_it_was_live()
    {
        Write(MigrationResults.Create())[0].ShouldBe($"Legacy migration run {MigrationResults.RunId} (live run)");
    }

    [Fact]
    public void The_report_names_a_dry_run()
    {
        Write(MigrationResults.Create(isDryRun: true, committed: false))[0].ShouldBe($"Legacy migration run {MigrationResults.RunId} (dry run)");
    }

    [Fact]
    public void The_report_has_the_row_bank_and_field_sections_then_the_conclusion()
    {
        var lines = Write(MigrationResults.Create());

        lines.Where(l => l.Length > 0 && !l.StartsWith(' ')).Skip(1).ShouldBe(
        [
            "Rows by file",
            "Returns by bank, source against target",
            "Field totals over every bank and period, source against target",
            LegacyConsoleReport.Conclusion(MigrationResults.Create()),
        ]);
        lines[^1].ShouldBe(LegacyConsoleReport.Conclusion(MigrationResults.Create()));
    }

    [Fact]
    public void Rows_are_accounted_for_per_file_and_for_all_files()
    {
        var lines = Write(MigrationResults.Create());

        Section(lines, "Rows by file").ShouldBe(
        [
            "  File       Type  Rows  Blank  Superseded  Rejected  Migrated  Already migrated",
            "  mlr.csv    MLR      6      1           1         1         2                 1",
            "  qcar.csv   QCAR     3      0           0         0         3                 0",
            "  All files           9      1           1         1         5                 1",
        ]);
    }

    [Fact]
    public void Returns_are_counted_per_bank_with_their_differences()
    {
        var result = MigrationResults.Create(MigrationOutcome.Mismatch, committed: false, reconciliation: MigrationResults.Mismatched);

        Section(Write(result), "Returns by bank, source against target").ShouldBe(
        [
            "  Type  Bank  Source  Target  Differences  Status",
            "  MLR   CCB        1       0            2  MISMATCH",
            "  MLR   HLB        1       1            1  MISMATCH",
            "  QCAR  HLB        1       1            0  Match",
        ]);
    }

    [Fact]
    public void Field_totals_cover_every_bank_and_period_with_amounts_to_two_decimals()
    {
        var result = MigrationResults.Create(MigrationOutcome.Mismatch, committed: false, reconciliation: MigrationResults.Mismatched);

        Section(Write(result), "Field totals over every bank and period, source against target").ShouldBe(
        [
            "  Type  Field       Returns  Source total  Target total  Difference  Status",
            "  MLR   TOTAL_HQLA        2      2,234.50      1,234.25   -1,000.25  MISMATCH",
            "  MLR   LCR               2        230.00        120.00     -110.00  MISMATCH",
            "  QCAR  CAR               1         15.50         15.50        0.00  Match",
        ]);
    }

    [Fact]
    public void A_run_with_nothing_to_reconcile_has_no_bank_or_field_section()
    {
        var result = MigrationResults.Create(reconciliation: Reconciliation.Compare([], []));

        var lines = Write(result);

        lines.ShouldNotContain("Returns by bank, source against target");
        lines.ShouldNotContain("Field totals over every bank and period, source against target");
        lines[^1].ShouldBe(LegacyConsoleReport.Conclusion(result));
    }

    [Fact]
    public void A_reconciled_live_run_says_it_committed()
    {
        LegacyConsoleReport.Conclusion(MigrationResults.Create(totals: Clean))
            .ShouldBe("Reconciled and committed: 7 return(s) migrated, 1 already migrated.");
    }

    [Fact]
    public void A_reconciled_dry_run_says_everything_was_rolled_back()
    {
        LegacyConsoleReport.Conclusion(MigrationResults.Create(isDryRun: true, committed: false, totals: Clean))
            .ShouldBe("Reconciled: 7 return(s) would be migrated, 1 already migrated. Dry run, so everything was rolled back.");
    }

    [Fact]
    public void A_conclusion_points_to_the_error_report_when_rows_were_left_out()
    {
        LegacyConsoleReport.Conclusion(MigrationResults.Create())
            .ShouldBe("Reconciled and committed: 5 return(s) migrated, 1 already migrated." + Excluded);
    }

    [Fact]
    public void A_mismatch_says_how_many_differences_and_that_nothing_was_committed()
    {
        var result = MigrationResults.Create(MigrationOutcome.Mismatch, committed: false, reconciliation: MigrationResults.Mismatched);

        LegacyConsoleReport.Conclusion(result)
            .ShouldBe("MISMATCH: 3 difference(s) between source and target. Nothing was committed." + Excluded);
    }

    [Fact]
    public void A_mismatch_says_when_the_rows_do_not_add_up()
    {
        var result = MigrationResults.Create(MigrationOutcome.Mismatch, committed: false, totals: Clean with { RowsRead = 9 });

        LegacyConsoleReport.Conclusion(result)
            .ShouldBe("MISMATCH: 0 difference(s) between source and target, and the rows do not add up. Nothing was committed.");
    }

    [Fact]
    public void Counts_use_thousand_separators()
    {
        var result = MigrationResults.Create(totals: new MigrationTotals(12345, 0, 0, 0, 12345, 0));

        Write(result).ShouldContain(l => l.StartsWith("  All files", StringComparison.Ordinal) && l.Contains("12,345", StringComparison.Ordinal));
    }

    private static string[] Write(LegacyMigrationResult result)
    {
        using var output = new StringWriter { NewLine = "\n" };
        LegacyConsoleReport.Write(result, output);
        return output.ToString().TrimEnd('\n').Split('\n');
    }

    private static string[] Section(string[] lines, string title) =>
        [.. lines.SkipWhile(l => l != title).Skip(1).TakeWhile(l => l.Length > 0)];
}
