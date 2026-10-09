using System.Globalization;
using System.Text;

using RegReturns.Application.Migration;
using RegReturns.Domain.Migration;
using RegReturns.Infrastructure.Legacy;
using RegReturns.UnitTests.TestSupport;

namespace RegReturns.UnitTests.Infrastructure.Legacy;

public sealed class MigrationReportWriterTests : IDisposable
{
    private const string Run = "0199b2c3-d4e5-7f60-8a7b-9c0d1e2f3a4b";

    private readonly string _directory = Directory.CreateTempSubdirectory("regreturns-migration-report-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task WriteAsync_writes_the_five_reports_into_a_new_folder()
    {
        var folder = Path.Combine(_directory, "reports", "run-1");

        var written = await new MigrationReportWriter().WriteAsync(MigrationResults.Create(), folder, TestContext.Current.CancellationToken);

        written.ShouldBe(
        [
            Path.Combine(folder, "summary.csv"),
            Path.Combine(folder, "row-errors.csv"),
            Path.Combine(folder, "reconciliation-detail.csv"),
            Path.Combine(folder, "reconciliation-by-bank.csv"),
            Path.Combine(folder, "reconciliation-by-period.csv"),
        ]);
        written.ShouldAllBe(path => File.Exists(path));
    }

    [Fact]
    public async Task Reports_are_utf8_with_a_byte_order_mark_so_spreadsheets_read_them_as_utf8()
    {
        var written = await new MigrationReportWriter().WriteAsync(MigrationResults.Create(), _directory, TestContext.Current.CancellationToken);

        foreach (var path in written)
        {
            var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            bytes[..3].ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF }, path);
        }

        var summary = await File.ReadAllBytesAsync(written[0], TestContext.Current.CancellationToken);
        Encoding.UTF8.GetString(summary.AsSpan(3)).ShouldBe(MigrationReportWriter.Summary(MigrationResults.Create()));
    }

    [Fact]
    public async Task Writing_again_replaces_the_earlier_reports()
    {
        var writer = new MigrationReportWriter();
        var ct = TestContext.Current.CancellationToken;
        await writer.WriteAsync(MigrationResults.Create(errors: MigrationResults.Errors), _directory, ct);

        await writer.WriteAsync(MigrationResults.Create(errors: []), _directory, ct);

        var errors = await File.ReadAllTextAsync(Path.Combine(_directory, MigrationReportWriter.ErrorsFile), ct);
        errors.ShouldBe("File,Line,Kind,Code,Column,Value,Message\r\n");
    }

    [Fact]
    public void The_summary_has_a_line_per_file_and_a_total_line_with_the_mismatches()
    {
        var summary = MigrationReportWriter.Summary(MigrationResults.Create(isDryRun: true, committed: false));

        Lines(summary).ShouldBe(
        [
            "RunId,DryRun,Committed,Outcome,File,ReturnType,Sha256,RowsRead,BlankRows,SupersededRows,RejectedRows,MigratedReturns,AlreadyMigratedReturns,Mismatches",
            $"{Run},true,false,Reconciled,mlr.csv,MLR,{MigrationResults.MlrSha},6,1,1,1,2,1,",
            $"{Run},true,false,Reconciled,qcar.csv,QCAR,{MigrationResults.QcarSha},3,0,0,0,3,0,",
            $"{Run},true,false,Reconciled,ALL,,,9,1,1,1,5,1,0",
        ]);
    }

    [Fact]
    public void The_summary_total_line_carries_the_outcome_and_the_reconciliation_differences()
    {
        var result = MigrationResults.Create(MigrationOutcome.Mismatch, committed: false, reconciliation: MigrationResults.Mismatched);

        Lines(MigrationReportWriter.Summary(result))[^1].ShouldBe($"{Run},false,false,Mismatch,ALL,,,9,1,1,1,5,1,3");
    }

    [Fact]
    public void The_error_report_lists_each_row_error_with_its_column_and_value()
    {
        var errors = MigrationReportWriter.Errors(MigrationResults.Create());

        Lines(errors).ShouldBe(
        [
            "File,Line,Kind,Code,Column,Value,Message",
            "mlr.csv,3,Superseded,Legacy.Superseded,,,\"Replaced by line 6, a later row for HLB MLR 2024-01; the last row wins.\"",
            "mlr.csv,5,Rejected,Legacy.BadNumber,Lvl 1 Assets,'=SUM(A1:A9),The value of L1_HQLA is not a number.",
        ]);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x.example\")", "\"'=HYPERLINK(\"\"http://x.example\"\")\"")]
    [InlineData("+44 20 7946 0000", "'+44 20 7946 0000")]
    [InlineData("-1,234", "\"'-1,234\"")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("-12.5", "-12.5")]
    [InlineData("1.234,56", "\"1.234,56\"")]
    public void Legacy_values_echoed_in_the_error_report_cannot_run_as_formulas(string value, string cell)
    {
        var result = MigrationResults.Create(errors:
            [new LegacyRowError("mlr.csv", 4, RowErrorKind.Rejected, MigrationErrors.BadNumber, "Not a number.", "Assets", value)]);

        Lines(MigrationReportWriter.Errors(result))[1].ShouldBe($"mlr.csv,4,Rejected,Legacy.BadNumber,Assets,{cell},Not a number.");
    }

    [Fact]
    public void The_detail_report_has_a_line_per_field_with_source_target_difference_and_status()
    {
        var detail = MigrationReportWriter.Detail(MigrationResults.Mismatched);

        Lines(detail).ShouldBe(
        [
            "ReturnType,Bank,Period,Field,Source,Target,Difference,Status",
            "MLR,CCB,2024-01,TOTAL_HQLA,1000,,,MissingInTarget",
            "MLR,CCB,2024-01,LCR,110,,,MissingInTarget",
            "MLR,HLB,2024-01,TOTAL_HQLA,1234.5,1234.25,-0.25,Mismatch",
            "MLR,HLB,2024-01,LCR,120,120,0,Match",
            "QCAR,HLB,2024-Q1,CAR,15.5,15.5,0,Match",
        ]);
    }

    [Fact]
    public void The_bank_totals_report_names_its_group_column_bank()
    {
        var totals = MigrationReportWriter.Totals("Bank", MigrationResults.Mismatched.ByInstitution());

        Lines(totals).ShouldBe(
        [
            "ReturnType,Bank,Field,SourceReturns,TargetReturns,SourceTotal,TargetTotal,Difference,Status",
            "MLR,CCB,TOTAL_HQLA,1,0,1000,0,-1000,Mismatch",
            "MLR,CCB,LCR,1,0,110,0,-110,Mismatch",
            "MLR,HLB,TOTAL_HQLA,1,1,1234.5,1234.25,-0.25,Mismatch",
            "MLR,HLB,LCR,1,1,120,120,0,Match",
            "QCAR,HLB,CAR,1,1,15.5,15.5,0,Match",
        ]);
    }

    [Fact]
    public void The_period_totals_report_names_its_group_column_period()
    {
        var totals = MigrationReportWriter.Totals("Period", MigrationResults.Reconciled.ByPeriod());

        Lines(totals).ShouldBe(
        [
            "ReturnType,Period,Field,SourceReturns,TargetReturns,SourceTotal,TargetTotal,Difference,Status",
            "MLR,2024-01,TOTAL_HQLA,2,2,2234.5,2234.5,0,Match",
            "MLR,2024-01,LCR,2,2,230,230,0,Match",
            "QCAR,2024-Q1,CAR,1,1,15.5,15.5,0,Match",
        ]);
    }

    [Fact]
    public void Numbers_are_written_in_invariant_culture_without_trailing_zeros_in_any_culture()
    {
        var reconciliation = Reconciliation.Compare(
            [new(new("MLR", "HLB", "2024-01"), new Dictionary<string, decimal?> { ["A"] = 1234567.50m, ["B"] = 0.12345678901234m })],
            [new(new("MLR", "HLB", "2024-01"), new Dictionary<string, decimal?> { ["A"] = 1234567.50m, ["B"] = 0.12345678901234m })]);
        var original = CultureInfo.CurrentCulture;
        string detail;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            detail = MigrationReportWriter.Detail(reconciliation);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        Lines(detail)[1..].ShouldBe(
        [
            "MLR,HLB,2024-01,A,1234567.5,1234567.5,0,Match",
            "MLR,HLB,2024-01,B,0.123456789,0.123456789,0,Match",
        ]);
    }

    private static string[] Lines(string csv)
    {
        csv.ShouldEndWith("\r\n");
        return csv[..^2].Split("\r\n");
    }
}
