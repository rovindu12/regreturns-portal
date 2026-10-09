extern alias MigratorTool;

using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;

using MigratorTool::RegReturns.Migrator.Commands;

using NSubstitute;

using RegReturns.Application.Migration;
using RegReturns.Domain.Common;
using RegReturns.Domain.Migration;
using RegReturns.Infrastructure.Legacy;
using RegReturns.UnitTests.TestSupport;

namespace RegReturns.UnitTests.Migrator;

public sealed class LegacyCommandsTests : IDisposable
{
    private static readonly LegacyMigrationRequest Request = new("samples/legacy", "samples/legacy/mapping.json", DryRun: true);

    private readonly string _directory = Directory.CreateTempSubdirectory("regreturns-legacy-commands-").FullName;
    private readonly ILegacyMigrator _migrator = Substitute.For<ILegacyMigrator>();
    private readonly IMigrationReportWriter _writer = Substitute.For<IMigrationReportWriter>();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Legacy_reads_the_source_dry_run_and_report_options()
    {
        var parse = Parse("legacy", "--source", "exports", "--dry-run", "--report", "reports/run-1");

        parse.Errors.ShouldBeEmpty();
        parse.CommandResult.Command.Name.ShouldBe("legacy");
        parse.GetValue<DirectoryInfo>("--source")!.ToString().ShouldBe("exports");
        parse.GetValue<bool>("--dry-run").ShouldBeTrue();
        parse.GetValue<DirectoryInfo?>("--report")!.ToString().ShouldBe("reports/run-1");
    }

    [Fact]
    public void Legacy_is_a_live_run_without_reports_and_with_the_default_mapping_unless_told_otherwise()
    {
        var parse = Parse("legacy", "--source", "exports");

        parse.Errors.ShouldBeEmpty();
        parse.GetValue<bool>("--dry-run").ShouldBeFalse();
        parse.GetValue<DirectoryInfo?>("--report").ShouldBeNull();
        parse.GetValue<FileInfo?>("--mapping").ShouldBeNull();
        LegacyCommands.DefaultMappingFile.ShouldBe("mapping.json");
    }

    [Fact]
    public void Legacy_takes_a_mapping_file_from_elsewhere()
    {
        var parse = Parse("legacy", "--source", "exports", "--mapping", "config/vrrs.json");

        parse.GetValue<FileInfo?>("--mapping")!.ToString().ShouldBe("config/vrrs.json");
    }

    [Fact]
    public void Legacy_needs_a_source_folder()
    {
        Parse("legacy", "--dry-run").Errors.ShouldContain(e => e.Message.Contains("--source", StringComparison.Ordinal));
    }

    [Fact]
    public void Legacy_samples_reads_the_output_folder()
    {
        var parse = Parse("legacy-samples", "--out", "samples/legacy");

        parse.Errors.ShouldBeEmpty();
        parse.CommandResult.Command.Name.ShouldBe("legacy-samples");
        parse.GetValue<DirectoryInfo>("--out")!.ToString().ShouldBe("samples/legacy");
    }

    [Fact]
    public void Legacy_samples_needs_an_output_folder()
    {
        Parse("legacy-samples").Errors.ShouldContain(e => e.Message.Contains("--out", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(MigrationOutcome.Reconciled, LegacyCommands.Reconciled)]
    [InlineData(MigrationOutcome.Mismatch, LegacyCommands.Mismatch)]
    public async Task The_exit_code_follows_the_outcome(MigrationOutcome outcome, int exitCode)
    {
        Returns(MigrationResults.Create(outcome));

        (await MigrateAsync(reportFolder: null)).ExitCode.ShouldBe(exitCode);
    }

    [Fact]
    public void Exit_codes_are_zero_for_reconciled_one_for_failed_and_two_for_mismatch()
    {
        (LegacyCommands.Reconciled, LegacyCommands.Failed, LegacyCommands.Mismatch).ShouldBe((0, 1, 2));
    }

    [Fact]
    public async Task A_run_that_cannot_start_fails_with_the_reason()
    {
        Returns(MigrationErrors.MappingInvalid.WithMessage("The mapping file is not valid: List at least one file."));

        var (exitCode, output) = await MigrateAsync(reportFolder: null);

        exitCode.ShouldBe(LegacyCommands.Failed);
        output.ShouldBe("The migration could not start: The mapping file is not valid: List at least one file. (Migration.MappingInvalid)\n");
        await _writer.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_request_reaches_the_migrator_unchanged()
    {
        Returns(MigrationResults.Create());

        await MigrateAsync(reportFolder: null);

        await _migrator.Received(1).RunAsync(Request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_finished_run_prints_the_console_report()
    {
        Returns(MigrationResults.Create());

        var (_, output) = await MigrateAsync(reportFolder: null);

        output.ShouldStartWith($"Legacy migration run {MigrationResults.RunId} (live run)\n");
        output.ShouldEndWith("see row-errors.csv in the report folder.\n");
    }

    [Fact]
    public async Task Reports_are_written_only_when_a_report_folder_is_given()
    {
        Returns(MigrationResults.Create());

        await MigrateAsync(reportFolder: null);

        await _writer.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reports_are_written_to_the_report_folder_and_listed()
    {
        var result = MigrationResults.Create(MigrationOutcome.Mismatch, committed: false);
        Returns(result);
        IReadOnlyList<string> written = [Path.Combine("out", "summary.csv"), Path.Combine("out", "row-errors.csv")];
        _writer.WriteAsync(result, "out", Arg.Any<CancellationToken>()).Returns(written);

        var (exitCode, output) = await MigrateAsync(reportFolder: "out");

        exitCode.ShouldBe(LegacyCommands.Mismatch);
        output.ShouldEndWith("Reports written to out: summary.csv, row-errors.csv.\n");
    }

    [Fact]
    public async Task Legacy_samples_writes_the_generated_files_and_succeeds()
    {
        var folder = Path.Combine(_directory, "samples", "legacy");
        using var output = new StringWriter { NewLine = "\n" };

        var exitCode = await LegacyCommands.WriteSamplesAsync(folder, output, TestContext.Current.CancellationToken);

        exitCode.ShouldBe(LegacyCommands.Reconciled);
        foreach (var (name, content) in LegacySamples.Generate())
        {
            (await File.ReadAllBytesAsync(Path.Combine(folder, name), TestContext.Current.CancellationToken)).ShouldBe(content, name);
        }

        output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ShouldBe(LegacySamples.Generate().Select(f => $"Wrote {Path.Combine(folder, f.Name)}"));
    }

    private static ParseResult Parse(params string[] args) =>
        new RootCommand { LegacyCommands.Legacy(), LegacyCommands.Samples() }.Parse(args);

    private void Returns(Result<LegacyMigrationResult> result) =>
        _migrator.RunAsync(Arg.Any<LegacyMigrationRequest>(), Arg.Any<CancellationToken>()).Returns(result);

    private async Task<(int ExitCode, string Output)> MigrateAsync(string? reportFolder)
    {
        await using var services = new ServiceCollection()
            .AddSingleton(_migrator)
            .AddSingleton(_writer)
            .BuildServiceProvider();
        using var output = new StringWriter { NewLine = "\n" };

        var exitCode = await LegacyCommands.MigrateAsync(services, Request, reportFolder, output, TestContext.Current.CancellationToken);

        return (exitCode, output.ToString());
    }
}
