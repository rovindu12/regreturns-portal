using RegReturns.Application.Migration;
using RegReturns.Domain.Common;
using RegReturns.Domain.Migration;
using RegReturns.Domain.Periods;
using RegReturns.Infrastructure.Legacy;

namespace RegReturns.UnitTests.Infrastructure.Legacy;

public sealed class LegacyRowReaderTests
{
    private const string GoodRow = "Harbourline Bank PLC,31/01/2024,2024-02-08 14:05:00,10/02/2024,\"VLD 1,234.50\",12.5%,";

    [Fact]
    public void A_clean_row_becomes_a_record_keyed_by_bank_code_and_period()
    {
        var record = Read(Table(GoodRow)).Records.ShouldHaveSingleItem();

        record.FileName.ShouldBe("returns.csv");
        record.LineNumber.ShouldBe(2);
        record.InstitutionCode.ShouldBe("HLB");
        record.Period.ShouldBe(ReportingPeriod.Monthly(2024, 1));
    }

    [Fact]
    public void Filing_and_approval_dates_are_read_as_utc()
    {
        var record = Read(Table(GoodRow)).Records.Single();

        record.SubmittedAt.ShouldBe(new DateTimeOffset(2024, 2, 8, 14, 5, 0, TimeSpan.Zero));
        record.ApprovedAt.ShouldBe(new DateTimeOffset(2024, 2, 10, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Values_are_cleansed_and_keyed_by_field_code_with_their_legacy_column()
    {
        var record = Read(Table(GoodRow)).Records.Single();

        record.Values.ShouldBe(new Dictionary<string, decimal?> { ["ASSETS"] = 1234.50m, ["RATIO"] = 12.5m });
        record.Columns.ShouldBe(new Dictionary<string, string> { ["ASSETS"] = "Assets", ["RATIO"] = "Ratio %" });
    }

    [Fact]
    public void A_null_token_is_a_blank_value_not_an_error()
    {
        var read = Read(Table("HLB,31/01/2024,08/02/2024,10/02/2024,N/A,12.5,"));

        read.Errors.ShouldBeEmpty();
        read.Records.Single().Values["ASSETS"].ShouldBeNull();
    }

    [Fact]
    public void A_quarterly_return_is_keyed_by_the_quarter_its_reporting_date_ends()
    {
        var read = Read(Table("HLB,31/03/2024,15/04/2024,20/04/2024,100,10,"), ReturnFrequency.Quarterly);

        read.Records.Single().Period.ShouldBe(ReportingPeriod.Quarterly(2024, 1));
    }

    [Fact]
    public void Header_columns_match_the_mapping_ignoring_case()
    {
        var header = LegacyTestData.Header.Select(h => h.ToUpperInvariant()).ToList();

        Read(LegacyTestData.Table(header, GoodRow)).Records.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_header_without_a_mapped_column_does_not_match_the_mapping()
    {
        var header = LegacyTestData.Header.Where(h => h != "Approved").ToList();

        Mismatch(LegacyTestData.Table(header)).ShouldBe("returns.csv does not match the mapping: it has no column 'Approved'.");
    }

    [Fact]
    public void A_header_with_a_column_the_mapping_does_not_know_does_not_match_the_mapping()
    {
        var header = LegacyTestData.Header.Append("Branch").ToList();

        Mismatch(LegacyTestData.Table(header)).ShouldBe(
            "returns.csv does not match the mapping: the mapping neither maps nor ignores its column 'Branch'.");
    }

    [Fact]
    public void A_header_naming_a_column_twice_does_not_match_the_mapping()
    {
        var header = LegacyTestData.Header.Append("BANK").ToList();

        Mismatch(LegacyTestData.Table(header)).ShouldBe("returns.csv does not match the mapping: its header names column 'BANK' twice.");
    }

    [Fact]
    public void A_row_with_too_few_or_too_many_cells_is_rejected()
    {
        var read = Read(Table("HLB,31/01/2024,08/02/2024", GoodRow + ",extra"));

        read.Errors.Select(e => (e.LineNumber, e.Code, e.Message)).ShouldBe(
        [
            (2, MigrationErrors.ColumnCount, "The row has 3 cells; the header has 7."),
            (3, MigrationErrors.ColumnCount, "The row has 8 cells; the header has 7."),
        ]);
        read.Records.ShouldBeEmpty();
    }

    [Fact]
    public void A_bank_name_the_mapping_does_not_know_is_rejected_with_the_name()
    {
        var error = Read(Table("Lotus Unoin Bank,31/01/2024,08/02/2024,10/02/2024,100,10,")).Errors.ShouldHaveSingleItem();

        error.Kind.ShouldBe(RowErrorKind.Rejected);
        error.Code.ShouldBe(MigrationErrors.UnknownInstitution);
        error.Field.ShouldBe("Bank");
        error.Value.ShouldBe("Lotus Unoin Bank");
    }

    [Fact]
    public void A_bank_the_portal_does_not_know_is_rejected_naming_its_code()
    {
        var error = Read(Table("Valoria Agricultural Bank,31/01/2024,08/02/2024,10/02/2024,100,10,")).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(MigrationErrors.InstitutionNotInPortal);
        error.Message.ShouldBe("The mapping names bank VAB, which is not in the portal.");
        error.Value.ShouldBe("Valoria Agricultural Bank");
    }

    [Theory]
    [InlineData("31-01-2024")]
    [InlineData("31/02/2024")]
    [InlineData("January 2024")]
    public void A_reporting_date_in_no_mapping_format_is_rejected(string date)
    {
        var error = Read(Table($"HLB,{date},08/02/2024,10/02/2024,100,10,")).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(MigrationErrors.BadDate);
        error.Message.ShouldBe("Period End is not a date in any of the mapping's formats.");
        error.Field.ShouldBe("Period End");
        error.Value.ShouldBe(date);
    }

    [Fact]
    public void A_reporting_date_that_is_not_a_month_end_is_rejected()
    {
        var error = Read(Table("HLB,15/11/2024,08/12/2024,10/12/2024,100,10,")).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(MigrationErrors.NotPeriodEnd);
        error.Message.ShouldBe("The reporting date is not the last day of a month.");
        error.Value.ShouldBe("15/11/2024");
    }

    [Fact]
    public void A_month_end_that_is_not_a_quarter_end_is_rejected_for_a_quarterly_return()
    {
        var error = Read(Table("HLB,29/02/2024,08/03/2024,10/03/2024,100,10,"), ReturnFrequency.Quarterly).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(MigrationErrors.NotPeriodEnd);
        error.Message.ShouldBe("The reporting date is not the last day of a quarter.");
    }

    [Fact]
    public void A_blank_filing_date_is_rejected()
    {
        var error = Read(Table("HLB,31/01/2024,,10/02/2024,100,10,")).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(MigrationErrors.BadDate);
        error.Message.ShouldBe("Filed is blank.");
        error.Field.ShouldBe("Filed");
    }

    [Fact]
    public void A_value_that_is_not_a_number_is_rejected_with_its_column_and_value()
    {
        var error = Read(Table("HLB,31/01/2024,08/02/2024,10/02/2024,\"1.234,56\",10,")).Errors.ShouldHaveSingleItem();

        error.Code.ShouldBe(MigrationErrors.BadNumber);
        error.Message.ShouldBe("The value of ASSETS is not a number.");
        error.Field.ShouldBe("Assets");
        error.Value.ShouldBe("1.234,56");
    }

    [Fact]
    public void Every_bad_cell_of_a_row_is_reported()
    {
        var read = Read(Table("HLB,31/01/2024,someday,10/02/2024,lots,ten,"));

        read.Errors.Select(e => (e.LineNumber, e.Code, e.Field)).ShouldBe(
        [
            (2, MigrationErrors.BadDate, "Filed"),
            (2, MigrationErrors.BadNumber, "Assets"),
            (2, MigrationErrors.BadNumber, "Ratio %"),
        ]);
        read.Records.ShouldBeEmpty();
    }

    [Fact]
    public void A_row_with_an_unknown_bank_and_a_bad_reporting_date_reports_both()
    {
        var read = Read(Table("Nobody,31/02/2024,08/02/2024,10/02/2024,100,10,"));

        read.Errors.Select(e => e.Code).ShouldBe([MigrationErrors.UnknownInstitution, MigrationErrors.BadDate]);
    }

    [Fact]
    public void Ignored_columns_are_not_read()
    {
        var read = Read(Table("HLB,31/01/2024,08/02/2024,10/02/2024,100,10,=see covering letter"));

        read.Errors.ShouldBeEmpty();
        read.Records.ShouldHaveSingleItem();
    }

    [Fact]
    public void Blank_rows_are_counted_and_skipped()
    {
        var table = LegacyTestData.Table(LegacyTestData.Header, GoodRow, ",,,,,,", string.Empty, "  ,  ,,,,,");

        var read = Read(table);

        read.BlankRows.ShouldBe(3);
        read.Errors.ShouldBeEmpty();
        read.Records.ShouldHaveSingleItem();
    }

    [Fact]
    public void The_last_row_for_a_bank_and_period_wins()
    {
        var read = Read(Table(
            "Harbourline Bank PLC,31/01/2024,08/02/2024,10/02/2024,100,10,",
            "CCB,31/01/2024,08/02/2024,10/02/2024,300,30,",
            "HARBOURLINE BANK PLC.,2024-01-31,20/02/2024,22/02/2024,150,15,Resubmitted"));

        read.Records.Select(r => (r.InstitutionCode, r.LineNumber, r.Values["ASSETS"])).ShouldBe([("HLB", 4, 150m), ("CCB", 3, 300m)], ignoreOrder: true);
    }

    [Fact]
    public void An_earlier_row_is_reported_as_superseded_by_the_winning_line()
    {
        var read = Read(Table(
            "HLB,31/01/2024,08/02/2024,10/02/2024,100,10,",
            "HLB,31/01/2024,09/02/2024,11/02/2024,120,12,",
            "HLB,31/01/2024,20/02/2024,22/02/2024,150,15,"));

        read.Errors.Select(e => (e.LineNumber, e.Kind, e.Code, e.Message)).ShouldBe(
        [
            (2, RowErrorKind.Superseded, MigrationErrors.Superseded, "Replaced by line 4, a later row for HLB MLR 2024-01; the last row wins."),
            (3, RowErrorKind.Superseded, MigrationErrors.Superseded, "Replaced by line 4, a later row for HLB MLR 2024-01; the last row wins."),
        ]);
        read.Records.ShouldHaveSingleItem().LineNumber.ShouldBe(4);
    }

    [Fact]
    public void Rows_for_other_periods_are_not_duplicates()
    {
        var read = Read(Table(
            "HLB,31/01/2024,08/02/2024,10/02/2024,100,10,",
            "HLB,29/02/2024,08/03/2024,10/03/2024,100,10,"));

        read.Errors.ShouldBeEmpty();
        read.Records.Count.ShouldBe(2);
    }

    [Fact]
    public void A_winning_row_that_fails_cleansing_still_supersedes_the_earlier_one()
    {
        var read = Read(Table(
            "HLB,31/01/2024,08/02/2024,10/02/2024,100,10,",
            "HLB,31/01/2024,20/02/2024,22/02/2024,lots,15,"));

        read.Errors.Select(e => (e.LineNumber, e.Kind, e.Code)).ShouldBe(
        [
            (2, RowErrorKind.Superseded, MigrationErrors.Superseded),
            (3, RowErrorKind.Rejected, MigrationErrors.BadNumber),
        ]);
        read.Records.ShouldBeEmpty();
    }

    [Fact]
    public void A_row_rejected_before_its_period_is_known_replaces_nothing()
    {
        var read = Read(Table(
            "HLB,31/01/2024,08/02/2024,10/02/2024,100,10,",
            "HLB,31-01-2024,20/02/2024,22/02/2024,150,15,"));

        read.Errors.ShouldHaveSingleItem().Code.ShouldBe(MigrationErrors.BadDate);
        read.Records.ShouldHaveSingleItem().LineNumber.ShouldBe(2);
    }

    [Fact]
    public void Errors_come_in_line_order()
    {
        var read = Read(Table(
            "HLB,31/01/2024,08/02/2024,10/02/2024,100,10,",
            "Nobody,31/01/2024,08/02/2024,10/02/2024,100,10,",
            "HLB,31/01/2024,20/02/2024,22/02/2024,150,15,",
            "CCB,31/01/2024,08/02/2024,10/02/2024,x,10,"));

        read.Errors.Select(e => (e.LineNumber, e.Code)).ShouldBe(
        [
            (2, MigrationErrors.Superseded),
            (3, MigrationErrors.UnknownInstitution),
            (5, MigrationErrors.BadNumber),
        ]);
    }

    private static LegacyTable Table(params string[] lines) => LegacyTestData.Table(lines);

    private static LegacyFileRead Read(LegacyTable table, ReturnFrequency frequency = ReturnFrequency.Monthly) =>
        Reader(table, frequency).Value;

    private static Result<LegacyFileRead> Reader(LegacyTable table, ReturnFrequency frequency = ReturnFrequency.Monthly) =>
        LegacyRowReader.Read(table, LegacyTestData.FileMapping, frequency, LegacyTestData.Rules, LegacyTestData.Lookup, LegacyTestData.PortalBanks);

    private static string Mismatch(LegacyTable table)
    {
        var error = Reader(table).Error.ShouldNotBeNull();
        error.Code.ShouldBe(MigrationErrors.SourceMismatch.Code);
        return error.Message;
    }
}
