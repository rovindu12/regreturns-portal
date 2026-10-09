extern alias MigratorTool;

using MigratorTool::RegReturns.Migrator.Legacy;

namespace RegReturns.UnitTests.Migrator;

public sealed class ConsoleTableTests
{
    [Fact]
    public void Columns_are_padded_to_their_widest_cell_and_indented_by_two_spaces()
    {
        var table = new ConsoleTable("File", "Type");
        table.Add("vrrs_mda_history.csv", "MDA");
        table.Add("mlr.csv", "MLR");

        Write(table).ShouldBe(
        [
            "  File                  Type",
            "  vrrs_mda_history.csv  MDA",
            "  mlr.csv               MLR",
        ]);
    }

    [Fact]
    public void A_header_starting_with_a_greater_than_sign_right_aligns_its_column_without_showing_the_sign()
    {
        var table = new ConsoleTable("Bank", ">Rows");
        table.Add("HLB", "6");
        table.Add("CCB", "1,234");

        Write(table).ShouldBe(
        [
            "  Bank   Rows",
            "  HLB       6",
            "  CCB   1,234",
        ]);
    }

    [Fact]
    public void A_header_wider_than_its_cells_sets_the_column_width()
    {
        var table = new ConsoleTable(">Already migrated", "Status");
        table.Add("1", "Match");

        Write(table).ShouldBe(
        [
            "  Already migrated  Status",
            "                 1  Match",
        ]);
    }

    [Fact]
    public void Lines_carry_no_trailing_spaces()
    {
        var table = new ConsoleTable("Bank", "Status");
        table.Add("HLB", "Match");
        table.Add("CCB", "MISMATCH");

        Write(table).ShouldAllBe(line => line == line.TrimEnd());
    }

    [Fact]
    public void A_table_without_rows_writes_its_header()
    {
        Write(new ConsoleTable("Type", ">Rows")).ShouldBe(["  Type  Rows"]);
    }

    [Fact]
    public void A_row_must_have_one_cell_per_column()
    {
        var table = new ConsoleTable("Bank", "Status");

        Should.Throw<ArgumentException>(() => table.Add("HLB")).ParamName.ShouldBe("cells");
    }

    private static string[] Write(ConsoleTable table)
    {
        using var output = new StringWriter { NewLine = "\n" };
        table.WriteTo(output);
        return output.ToString().TrimEnd('\n').Split('\n');
    }
}
