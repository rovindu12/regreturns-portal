using System.Text;

using RegReturns.Application.Migration;
using RegReturns.Domain.Periods;
using RegReturns.Infrastructure.Legacy;
using RegReturns.Infrastructure.Persistence.Seeding;

namespace RegReturns.UnitTests.Infrastructure.Legacy;

public sealed class LegacySamplesTests
{
    private const string SolutionFile = "RegReturns.slnx";

    private static readonly IReadOnlySet<string> PortalBanks = DemoBank.All.Select(b => b.Code).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Generating_twice_gives_the_same_files_byte_for_byte()
    {
        var first = LegacySamples.Generate();
        var second = LegacySamples.Generate();

        second.Select(f => f.Name).ShouldBe(first.Select(f => f.Name));
        for (var i = 0; i < first.Count; i++)
        {
            second[i].Content.ShouldBe(first[i].Content, first[i].Name);
        }
    }

    [Fact]
    public void The_samples_are_the_mapping_and_three_exports()
    {
        LegacySamples.Generate().Select(f => f.Name).ShouldBe(
        [
            LegacySampleGenerator.MappingFile,
            LegacySampleGenerator.MlrFile,
            LegacySampleGenerator.MdaFile,
            LegacySampleGenerator.QcarFile,
        ]);
    }

    [Fact]
    public void The_committed_samples_equal_the_generator_output_byte_for_byte()
    {
        var folder = SamplesFolder();

        foreach (var (name, content) in LegacySamples.Generate())
        {
            var path = Path.Combine(folder, name);
            File.Exists(path).ShouldBeTrue($"{path} is missing; run `regreturns-migrator legacy-samples --out samples/legacy`.");
            File.ReadAllBytes(path).ShouldBe(content, $"{name} differs from the generator; run `regreturns-migrator legacy-samples --out samples/legacy`.");
        }
    }

    [Fact]
    public void The_samples_folder_holds_no_csv_file_the_mapping_does_not_list()
    {
        var mapped = Mapping().Files.Select(f => f.File);

        Directory.EnumerateFiles(SamplesFolder(), "*.csv").Select(Path.GetFileName).ShouldBe(mapped, ignoreOrder: true);
    }

    [Fact]
    public void The_sample_mapping_is_valid_and_maps_every_export()
    {
        var mapping = Mapping();

        mapping.Files.Select(f => (f.File, f.ReturnType)).ShouldBe(
        [
            (LegacySampleGenerator.MlrFile, "MLR"),
            (LegacySampleGenerator.MdaFile, "MDA"),
            (LegacySampleGenerator.QcarFile, "QCAR"),
        ]);
        mapping.Institutions.Keys.ShouldBe([.. PortalBanks, LegacySampleGenerator.ClosedBankCode], ignoreOrder: true);
    }

    [Fact]
    public void The_mda_and_qcar_exports_have_a_byte_order_mark_and_crlf_line_ends()
    {
        foreach (var name in new[] { LegacySampleGenerator.MdaFile, LegacySampleGenerator.QcarFile })
        {
            var content = Sample(name);
            content[..3].ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF }, name);
            var text = Encoding.UTF8.GetString(content, 3, content.Length - 3);
            text.Split('\n')[..^1].ShouldAllBe(line => line.EndsWith('\r'), name);
        }
    }

    [Fact]
    public void The_mlr_export_has_no_byte_order_mark_and_lf_line_ends()
    {
        var content = Sample(LegacySampleGenerator.MlrFile);

        content[0].ShouldBe((byte)'I');
        content.ShouldNotContain((byte)'\r');
        content[^1].ShouldBe((byte)'\n');
    }

    [Fact]
    public void The_mlr_export_carries_its_planted_defects()
    {
        var read = ReadSample(LegacySampleGenerator.MlrFile, ReturnFrequency.Monthly);

        read.BlankRows.ShouldBe(1);
        Codes(read).ShouldBe(new Dictionary<string, int>
        {
            [MigrationErrors.InstitutionNotInPortal] = 3,
            [MigrationErrors.NotPeriodEnd] = 1,
            [MigrationErrors.BadNumber] = 1,
            [MigrationErrors.Superseded] = 1,
        }, ignoreOrder: true);
    }

    [Fact]
    public void The_mlr_export_keeps_a_formula_like_remark_in_its_ignored_column()
    {
        var table = LegacyCsvReader.Read(LegacySampleGenerator.MlrFile, Sample(LegacySampleGenerator.MlrFile)).Value;

        table.Rows.Select(r => r.Cells[^1]).ShouldContain("=see covering letter");
    }

    [Fact]
    public void The_mlr_export_has_a_straggler_from_before_the_current_forms()
    {
        var read = ReadSample(LegacySampleGenerator.MlrFile, ReturnFrequency.Monthly);

        read.Records.ShouldContain(r => r.Period == ReportingPeriod.Monthly(2023, 12));
    }

    [Fact]
    public void The_mda_export_carries_its_planted_defects()
    {
        var read = ReadSample(LegacySampleGenerator.MdaFile, ReturnFrequency.Monthly);

        read.BlankRows.ShouldBe(1);
        Codes(read).ShouldBe(new Dictionary<string, int>
        {
            [MigrationErrors.UnknownInstitution] = 1,
            [MigrationErrors.BadDate] = 1,
            [MigrationErrors.ColumnCount] = 1,
            [MigrationErrors.Superseded] = 1,
        }, ignoreOrder: true);
    }

    [Fact]
    public void The_mda_export_reads_excel_serial_and_upper_case_month_dates()
    {
        var table = LegacyCsvReader.Read(LegacySampleGenerator.MdaFile, Sample(LegacySampleGenerator.MdaFile)).Value;
        var read = ReadSample(LegacySampleGenerator.MdaFile, ReturnFrequency.Monthly);

        table.Rows[0].Cells[1].ShouldMatch(@"^\d{5}$");
        table.Rows[0].Cells[3].ShouldMatch(@"^\d{2}-[A-Z]{3}-\d{4}$");
        read.Records.ShouldContain(r => r.Period == ReportingPeriod.Monthly(2024, 1));
    }

    [Fact]
    public void The_mda_export_has_a_return_with_null_tokens_for_values()
    {
        var read = ReadSample(LegacySampleGenerator.MdaFile, ReturnFrequency.Monthly);

        read.Records.ShouldContain(r => r.Values.Values.Count(v => v == null) == 2);
    }

    [Fact]
    public void The_qcar_export_has_a_restatement_that_supersedes_the_first_filing()
    {
        var read = ReadSample(LegacySampleGenerator.QcarFile, ReturnFrequency.Quarterly);

        read.BlankRows.ShouldBe(0);
        var superseded = read.Errors.ShouldHaveSingleItem();
        superseded.Code.ShouldBe(MigrationErrors.Superseded);
        superseded.Message.ShouldContain("CCB QCAR 2024-Q3");
    }

    [Fact]
    public void The_qcar_export_has_a_capital_ratio_in_accounting_brackets()
    {
        var read = ReadSample(LegacySampleGenerator.QcarFile, ReturnFrequency.Quarterly);

        read.Records.ShouldContain(r => r.InstitutionCode == "NSB" && r.Values["CAR"] < 0);
    }

    [Fact]
    public void Every_sample_row_is_a_record_an_error_or_a_blank_row()
    {
        foreach (var (name, frequency) in new[]
                 {
                     (LegacySampleGenerator.MlrFile, ReturnFrequency.Monthly),
                     (LegacySampleGenerator.MdaFile, ReturnFrequency.Monthly),
                     (LegacySampleGenerator.QcarFile, ReturnFrequency.Quarterly),
                 })
        {
            var read = ReadSample(name, frequency);
            var errorLines = read.Errors.Select(e => e.LineNumber).Distinct().Count();

            (read.Records.Count + errorLines + read.BlankRows).ShouldBe(read.Table.Rows.Count, name);
        }
    }

    private static LegacyMapping Mapping() =>
        LegacyMapping.Parse(Sample(LegacySampleGenerator.MappingFile)).Value.Mapping;

    private static LegacyFileRead ReadSample(string name, ReturnFrequency frequency)
    {
        var mapping = Mapping();
        var table = LegacyCsvReader.Read(name, Sample(name)).Value;
        var file = mapping.Files.Single(f => f.File == name);
        return LegacyRowReader.Read(table, file, frequency, mapping.Rules, mapping.InstitutionLookup(), PortalBanks).Value;
    }

    private static Dictionary<string, int> Codes(LegacyFileRead read) =>
        read.Errors.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.Count());

    private static byte[] Sample(string name) => LegacySamples.Generate().Single(f => f.Name == name).Content;

    private static string SamplesFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return Path.Combine(directory.FullName, "samples", "legacy");
            }
        }

        throw new InvalidOperationException($"{SolutionFile} was not found above {AppContext.BaseDirectory}.");
    }
}
