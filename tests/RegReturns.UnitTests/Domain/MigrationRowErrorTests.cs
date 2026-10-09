using RegReturns.Domain.Common;
using RegReturns.Domain.Migration;

namespace RegReturns.UnitTests.Domain;

public sealed class MigrationRowErrorTests
{
    [Fact]
    public void Create_keeps_where_and_why_a_row_failed()
    {
        var error = MigrationRowError.Create(
            "VRRS_MLR_EXPORT.csv", 7, RowErrorKind.Rejected, "Legacy.BadNumber", "The value of L2A_HQLA is not a number.", "Lvl 2A Assets", "1.234,56");

        error.FileName.ShouldBe("VRRS_MLR_EXPORT.csv");
        error.LineNumber.ShouldBe(7);
        error.Kind.ShouldBe(RowErrorKind.Rejected);
        error.Code.ShouldBe("Legacy.BadNumber");
        error.Message.ShouldBe("The value of L2A_HQLA is not a number.");
        error.Field.ShouldBe("Lvl 2A Assets");
        error.Value.ShouldBe("1.234,56");
    }

    [Fact]
    public void A_superseded_row_needs_no_field_or_value()
    {
        var error = MigrationRowError.Create("a.csv", 2, RowErrorKind.Superseded, "Legacy.Superseded", "Replaced by line 9.");

        error.Field.ShouldBeNull();
        error.Value.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_field_is_no_field(string? field)
    {
        MigrationRowError.Create("a.csv", 2, RowErrorKind.Rejected, "Legacy.BadDate", "Blank.", field).Field.ShouldBeNull();
    }

    [Fact]
    public void The_field_is_trimmed()
    {
        MigrationRowError.Create("a.csv", 2, RowErrorKind.Rejected, "Legacy.BadDate", "Blank.", "  Received On ").Field
            .ShouldBe("Received On");
    }

    [Fact]
    public void An_empty_value_is_no_value()
    {
        MigrationRowError.Create("a.csv", 2, RowErrorKind.Rejected, "Legacy.BadDate", "Blank.", "Received On", string.Empty).Value
            .ShouldBeNull();
    }

    [Fact]
    public void A_value_of_spaces_is_kept_as_found()
    {
        MigrationRowError.Create("a.csv", 2, RowErrorKind.Rejected, "Legacy.BadDate", "Blank.", "Received On", "   ").Value
            .ShouldBe("   ");
    }

    [Fact]
    public void A_message_at_the_limit_is_kept_whole()
    {
        var message = new string('m', MigrationRowError.MessageMaxLength);

        MigrationRowError.Create("a.csv", 2, RowErrorKind.Rejected, "Legacy.Validation", message).Message.ShouldBe(message);
    }

    [Fact]
    public void A_long_message_is_cut_to_the_limit_with_an_ellipsis()
    {
        var message = "Total HQLA " + new string('x', MigrationRowError.MessageMaxLength);

        var kept = MigrationRowError.Create("a.csv", 2, RowErrorKind.Rejected, "Legacy.Validation", message).Message;

        kept.Length.ShouldBe(MigrationRowError.MessageMaxLength);
        kept.ShouldEndWith("…");
        kept.ShouldStartWith(message[..(MigrationRowError.MessageMaxLength - 1)]);
    }

    [Fact]
    public void A_long_value_is_cut_to_the_limit_with_an_ellipsis()
    {
        var value = new string('7', MigrationRowError.ValueMaxLength + 50);

        var kept = MigrationRowError.Create("a.csv", 2, RowErrorKind.Rejected, "Legacy.BadNumber", "Too long.", "Assets", value).Value!;

        kept.Length.ShouldBe(MigrationRowError.ValueMaxLength);
        kept.ShouldBe(new string('7', MigrationRowError.ValueMaxLength - 1) + "…");
    }

    [Fact]
    public void A_long_field_is_cut_to_the_limit_with_an_ellipsis()
    {
        var field = new string('f', MigrationRowError.FieldMaxLength + 1);

        var kept = MigrationRowError.Create("a.csv", 2, RowErrorKind.Rejected, "Legacy.BadNumber", "Too long.", field).Field!;

        kept.Length.ShouldBe(MigrationRowError.FieldMaxLength);
        kept.ShouldEndWith("…");
    }

    [Fact]
    public void A_file_name_longer_than_the_limit_is_refused()
    {
        var fileName = new string('f', MigrationRowError.FileNameMaxLength + 1);

        Should.Throw<DomainException>(() => MigrationRowError.Create(fileName, 2, RowErrorKind.Rejected, "Legacy.BadDate", "Bad."));
    }

    [Fact]
    public void A_code_longer_than_the_limit_is_refused()
    {
        var code = "Legacy." + new string('C', MigrationRowError.CodeMaxLength);

        Should.Throw<DomainException>(() => MigrationRowError.Create("a.csv", 2, RowErrorKind.Rejected, code, "Bad."));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_line_number_starts_at_one(int lineNumber)
    {
        Should.Throw<DomainException>(() => MigrationRowError.Create("a.csv", lineNumber, RowErrorKind.Rejected, "Legacy.BadDate", "Bad."));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_message_is_required(string message)
    {
        Should.Throw<DomainException>(() => MigrationRowError.Create("a.csv", 2, RowErrorKind.Rejected, "Legacy.BadDate", message));
    }
}
