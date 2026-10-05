using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Domain.Auditing;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// The audit area: changes made through the portal appear in the trail under the user who made them, with their
/// values, and the auditor can verify the chain, which reports the entry where tampering broke it.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class AuditPagesTests : IClassFixture<PortalDatabaseFixture>, IDisposable
{
    private static readonly DateTimeOffset Today = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Today) { AutoAdvanceAmount = TimeSpan.FromSeconds(1) };
    private readonly SqlServerFixture _sql;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly BankPortal _bank;
    private readonly SupervisionPortal _supervision;
    private readonly string _connectionString;

    public AuditPagesTests(PortalDatabaseFixture database, SqlServerFixture sql)
    {
        _sql = sql;
        _connectionString = database.ConnectionString;
        _factory = CreateHost(database.ConnectionString);
        _bank = new BankPortal(_factory, database.ConnectionString);
        _supervision = new SupervisionPortal(_factory, database.ConnectionString);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string MakerSubject => $"it-{DemoUsers.MakerUserName(DemoBank.Harbourline)}";

    [Fact]
    public async Task Auditor_sees_who_changed_a_return_and_the_values_they_entered()
    {
        var id = await DraftAsync(4);
        using var auditor = await _supervision.AuditorAsync();

        var page = await PortalForms.GetTextAsync(auditor, $"/audit?entityType={nameof(Submission)}&entityId={id}");

        page.ShouldContain(MakerSubject);
        page.ShouldContain($"Values[{MlrTemplate.LiquidAssets}]");
        page.ShouldContain(BankPortal.ValidMlr[MlrTemplate.LiquidAssets]!);
        var entries = await EntriesAsync(_connectionString, id);
        entries.Select(e => e.Action).ShouldContain(AuditAction.Created);
        entries.Select(e => e.Action).ShouldContain(AuditAction.Updated);
        entries.ShouldAllBe(e => e.ActorSubjectId == MakerSubject && e.InstitutionCode == DemoBank.Harbourline && e.CorrelationId != null);
    }

    [Fact]
    public async Task Submitting_a_return_is_recorded_as_a_status_change_by_the_checker()
    {
        var id = await DraftAsync(5);
        using var checker = await _bank.CheckerAsync();
        var page = $"/bank/returns/{id}";
        PortalForms.RedirectPath(await SupervisionPortal.PostStepAsync(checker, page, $"{page}/submit", "Checked.")).ShouldBe(page);

        var entry = (await EntriesAsync(_connectionString, id)).Where(e => e.Action == AuditAction.StateChanged).ShouldHaveSingleItem();

        entry.ActorSubjectId.ShouldBe($"it-{DemoUsers.CheckerUserName(DemoBank.Harbourline)}");
        entry.Details.ShouldBe($"Status changed from {SubmissionStatus.Draft} to {SubmissionStatus.Submitted}.");
        entry.OccurredAt.ShouldBeGreaterThan(Today);
    }

    [Fact]
    public async Task Verifying_an_intact_chain_says_so_and_records_the_check()
    {
        await DraftAsync(6);
        using var auditor = await _supervision.AuditorAsync();

        var response = await PortalForms.PostAsync(auditor, "/audit", "/audit/verify");

        (await PortalForms.FollowAsync(auditor, response)).ShouldContain("The audit chain is intact:");
        await using var context = SqlServerFixture.CreateContext(_connectionString);
        (await context.AuditEntries.AnyAsync(
            e => e.Action == AuditAction.ChainVerified && e.ActorSubjectId == $"it-{DemoUsers.Auditor}" && e.Details!.StartsWith("The audit chain is intact"),
            Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Verifying_a_tampered_chain_names_the_entry_that_breaks_it()
    {
        var database = new PortalDatabaseFixture(_sql);
        await database.InitializeAsync();
        using var factory = CreateHost(database.ConnectionString);
        using var auditor = await new SupervisionPortal(factory, database.ConnectionString).AuditorAsync();
        for (var i = 0; i < 3; i++)
        {
            PortalForms.RedirectPath(await PortalForms.PostAsync(auditor, "/audit", "/audit/verify")).ShouldBe("/audit");
        }

        await TamperAsync(database.ConnectionString, "UPDATE audit.AuditEntries SET Details = N'Nothing to see here.' WHERE Sequence = 2;");
        var response = await PortalForms.PostAsync(auditor, "/audit", "/audit/verify");

        var page = await PortalForms.FollowAsync(auditor, response);
        page.ShouldContain("The audit chain is broken at entry 2. Entry 2 does not match its hash");
        page.ShouldContain("alert-danger");
    }

    [Fact]
    public async Task Bank_staff_are_refused_the_audit_trail()
    {
        using var maker = await _bank.MakerAsync();

        var response = await maker.GetAsync(new Uri("/audit", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    public void Dispose() => _factory.Dispose();

    private WebApplicationFactory<Program> CreateHost(string connectionString) =>
        PortalHost.Create(connectionString, services => services.AddSingleton<TimeProvider>(_clock));

    /// <summary>A new Harbourline return with valid figures, saved by the maker.</summary>
    private async Task<Guid> DraftAsync(int month)
    {
        var obligationId = await _bank.NewMlrObligationAsync(month);
        using var maker = await _bank.MakerAsync();
        var id = await BankPortal.StartDraftAsync(maker, obligationId);
        (await BankPortal.SaveAsync(maker, id, BankPortal.ValidMlr)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return id;
    }

    private static async Task<List<AuditEntry>> EntriesAsync(string connectionString, Guid submissionId)
    {
        await using var context = SqlServerFixture.CreateContext(connectionString);
        var entityId = submissionId.ToString();
        return await context.AuditEntries.AsNoTracking()
            .Where(e => e.EntityType == nameof(Submission) && e.EntityId == entityId)
            .OrderBy(e => e.Sequence)
            .ToListAsync(Ct);
    }

    private static async Task TamperAsync(string connectionString, string tamperSql)
    {
        await using var context = SqlServerFixture.CreateContext(connectionString);
        await context.Database.ExecuteSqlRawAsync("DISABLE TRIGGER audit.TR_AuditEntries_AppendOnly ON audit.AuditEntries;", Ct);
        try
        {
            await context.Database.ExecuteSqlRawAsync(tamperSql, Ct);
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("ENABLE TRIGGER audit.TR_AuditEntries_AppendOnly ON audit.AuditEntries;", CancellationToken.None);
        }
    }
}
