extern alias MigratorTool;

using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using MigratorTool::RegReturns.Migrator.Auditing;
using MigratorTool::RegReturns.Migrator.Commands;

using NSubstitute;

using RegReturns.Application.Auditing;
using RegReturns.Domain.Auditing;

namespace RegReturns.UnitTests.Migrator;

public sealed class AuditCommandsTests
{
    private const string HeadHash = "9f2c";

    private readonly IAuditChainVerifier _verifier = Substitute.For<IAuditChainVerifier>();
    private readonly IAuditTrail _trail = Substitute.For<IAuditTrail>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Verify_audit_takes_no_options()
    {
        var parse = new RootCommand { AuditCommands.VerifyAudit() }.Parse(["verify-audit"]);

        parse.Errors.ShouldBeEmpty();
        parse.CommandResult.Command.Name.ShouldBe("verify-audit");
    }

    [Fact]
    public void Exit_codes_are_zero_for_intact_one_for_failed_and_two_for_broken()
    {
        (AuditCommands.Intact, AuditCommands.Failed, AuditCommands.Broken).ShouldBe((0, 1, 2));
    }

    [Fact]
    public async Task An_intact_chain_exits_with_zero_and_prints_the_head()
    {
        Verifies(new ChainVerification(42, 42, HeadHash, null));

        var (exitCode, output) = await VerifyAsync();

        exitCode.ShouldBe(AuditCommands.Intact);
        output.ShouldBe($"The audit chain is intact: 42 entries checked. The newest entry is 42, with hash {HeadHash}.\n");
    }

    [Fact]
    public async Task A_broken_chain_exits_with_two_and_explains_the_break()
    {
        Verifies(new ChainVerification(7, 42, HeadHash, ChainBreak.Edited(7)));

        var (exitCode, output) = await VerifyAsync();

        exitCode.ShouldBe(AuditCommands.Broken);
        output.ShouldStartWith("The audit chain is broken at entry 7. Entry 7 does not match its hash");
    }

    [Fact]
    public async Task The_check_is_recorded_in_the_chain_as_the_migrator()
    {
        Verifies(new ChainVerification(42, 42, HeadHash, null));

        await VerifyAsync();

        await _trail.Received(1).RecordAsync(
            Arg.Is<AuditRecord>(r =>
                r.Action == AuditAction.ChainVerified
                && r.ActorType == ActorType.System
                && r.ActorSubjectId == MigratorAuditContext.Subject
                && r.ActorDisplayName == "Audit chain verification"),
            Arg.Any<CancellationToken>());
    }

    private void Verifies(ChainVerification result) => _verifier.VerifyAsync(Arg.Any<CancellationToken>()).Returns(result);

    private async Task<(int ExitCode, string Output)> VerifyAsync()
    {
        await using var services = new ServiceCollection()
            .AddSingleton(_verifier)
            .AddSingleton(_trail)
            .AddSingleton<IAuditContext>(MigratorAuditContext.ChainVerification)
            .BuildServiceProvider();
        using var output = new StringWriter { NewLine = "\n" };

        var exitCode = await AuditCommands.VerifyAsync(services, NullLogger.Instance, output, Ct);

        return (exitCode, output.ToString());
    }
}
