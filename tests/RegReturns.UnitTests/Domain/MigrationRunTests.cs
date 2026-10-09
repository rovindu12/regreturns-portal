using RegReturns.Domain.Common;
using RegReturns.Domain.Migration;

namespace RegReturns.UnitTests.Domain;

public sealed class MigrationRunTests
{
    private const string MappingSha = "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";
    private const string FileSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static readonly DateTimeOffset Started = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Finished = Started.AddMinutes(3);

    // 10 rows read: 1 blank, 1 superseded, 2 rejected, 5 migrated and 1 already migrated.
    private static readonly MigrationTotals Balanced = new(10, 1, 1, 2, 5, 1);

    [Fact]
    public void Start_records_the_source_mapping_and_mode()
    {
        var run = MigrationRun.Start("  samples/legacy ", MappingSha, isDryRun: true, Started);

        run.Source.ShouldBe("samples/legacy");
        run.MappingSha256.ShouldBe(MappingSha);
        run.IsDryRun.ShouldBeTrue();
        run.StartedAt.ShouldBe(Started);
    }

    [Fact]
    public void A_started_run_has_no_outcome_and_no_counts()
    {
        var run = MigrationRun.Start("samples/legacy", MappingSha, isDryRun: false, Started);

        run.Outcome.ShouldBeNull();
        run.FinishedAt.ShouldBeNull();
        run.RowsRead.ShouldBe(0);
        run.Mismatches.ShouldBe(0);
        run.Files.ShouldBeEmpty();
        run.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("0F1E2D3C4B5A69788796A5B4C3D2E1F00F1E2D3C4B5A69788796A5B4C3D2E1F0")]
    [InlineData("0f1e2d3c4b5a69788796a5b4c3d2e1f0")]
    [InlineData("0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1fg")]
    [InlineData(" 0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f")]
    public void A_mapping_digest_must_be_64_lowercase_hex_characters(string digest)
    {
        Should.Throw<DomainException>(() => MigrationRun.Start("samples/legacy", digest, isDryRun: false, Started));
    }

    [Fact]
    public void A_run_needs_a_source()
    {
        Should.Throw<DomainException>(() => MigrationRun.Start("  ", MappingSha, isDryRun: false, Started));
    }

    [Fact]
    public void AddFile_records_the_file_and_its_digest()
    {
        var run = MigrationRun.Start("samples/legacy", MappingSha, isDryRun: false, Started);

        run.AddFile(" VRRS_MLR_EXPORT.csv ", "MLR", FileSha, 111);

        var file = run.Files.ShouldHaveSingleItem();
        file.FileName.ShouldBe("VRRS_MLR_EXPORT.csv");
        file.ReturnTypeCode.ShouldBe("MLR");
        file.Sha256.ShouldBe(FileSha);
        file.Rows.ShouldBe(111);
    }

    [Fact]
    public void A_file_cannot_have_a_negative_number_of_rows()
    {
        var run = MigrationRun.Start("samples/legacy", MappingSha, isDryRun: false, Started);

        Should.Throw<DomainException>(() => run.AddFile("a.csv", "MLR", FileSha, -1));
    }

    [Fact]
    public void A_file_digest_must_be_64_lowercase_hex_characters()
    {
        var run = MigrationRun.Start("samples/legacy", MappingSha, isDryRun: false, Started);

        Should.Throw<DomainException>(() => run.AddFile("a.csv", "MLR", FileSha.ToUpperInvariant(), 1));
    }

    [Fact]
    public void AddError_records_the_row_error()
    {
        var run = MigrationRun.Start("samples/legacy", MappingSha, isDryRun: false, Started);
        var error = MigrationRowError.Create("a.csv", 3, RowErrorKind.Rejected, "Legacy.BadNumber", "Not a number.");

        run.AddError(error);

        run.Errors.ShouldBe([error]);
    }

    [Fact]
    public void Finish_copies_the_row_accounting()
    {
        var run = MigrationRun.Start("samples/legacy", MappingSha, isDryRun: false, Started);

        run.Finish(Balanced, mismatches: 0, Finished);

        run.RowsRead.ShouldBe(10);
        run.BlankRows.ShouldBe(1);
        run.SupersededRows.ShouldBe(1);
        run.RejectedRows.ShouldBe(2);
        run.MigratedReturns.ShouldBe(5);
        run.AlreadyMigratedReturns.ShouldBe(1);
        run.FinishedAt.ShouldBe(Finished);
    }

    [Fact]
    public void A_run_without_mismatches_whose_rows_balance_is_reconciled()
    {
        var run = MigrationRun.Start("samples/legacy", MappingSha, isDryRun: true, Started);

        run.Finish(Balanced, mismatches: 0, Finished);

        run.Outcome.ShouldBe(MigrationOutcome.Reconciled);
        run.Mismatches.ShouldBe(0);
    }

    [Fact]
    public void A_run_with_a_reconciliation_difference_is_a_mismatch()
    {
        var run = MigrationRun.Start("samples/legacy", MappingSha, isDryRun: false, Started);

        run.Finish(Balanced, mismatches: 2, Finished);

        run.Outcome.ShouldBe(MigrationOutcome.Mismatch);
        run.Mismatches.ShouldBe(2);
    }

    [Fact]
    public void A_run_whose_rows_do_not_balance_is_a_mismatch_even_without_differences()
    {
        var run = MigrationRun.Start("samples/legacy", MappingSha, isDryRun: false, Started);

        run.Finish(Balanced with { RowsRead = 11 }, mismatches: 0, Finished);

        run.Outcome.ShouldBe(MigrationOutcome.Mismatch);
    }

    [Fact]
    public void Mismatches_cannot_be_negative()
    {
        var run = MigrationRun.Start("samples/legacy", MappingSha, isDryRun: false, Started);

        Should.Throw<DomainException>(() => run.Finish(Balanced, mismatches: -1, Finished));
        run.Outcome.ShouldBeNull();
    }

    [Fact]
    public void A_finished_run_takes_no_more_files_errors_or_results()
    {
        var run = MigrationRun.Start("samples/legacy", MappingSha, isDryRun: false, Started);
        run.Finish(Balanced, mismatches: 0, Finished);
        var error = MigrationRowError.Create("a.csv", 3, RowErrorKind.Rejected, "Legacy.BadNumber", "Not a number.");

        Should.Throw<DomainException>(() => run.AddFile("a.csv", "MLR", FileSha, 1));
        Should.Throw<DomainException>(() => run.AddError(error));
        Should.Throw<DomainException>(() => run.Finish(Balanced, mismatches: 0, Finished));
    }

    [Theory]
    [InlineData(10, 1, 1, 2, 5, 1, true)]
    [InlineData(0, 0, 0, 0, 0, 0, true)]
    [InlineData(10, 1, 1, 2, 5, 0, false)]
    [InlineData(10, 1, 1, 2, 5, 2, false)]
    public void Totals_balance_when_every_row_read_is_accounted_for_once(
        int read, int blank, int superseded, int rejected, int migrated, int alreadyMigrated, bool balanced)
    {
        new MigrationTotals(read, blank, superseded, rejected, migrated, alreadyMigrated).IsBalanced.ShouldBe(balanced);
    }
}
