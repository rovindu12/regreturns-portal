using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Application.Auditing;
using RegReturns.Infrastructure.Persistence;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;
using RegReturns.IntegrationTests.Web;
using RegReturns.Web.Navigation;

namespace RegReturns.IntegrationTests.Database;

/// <summary>
/// The <c>regreturns_runtime</c> role the production apps log in with (ADR 0034): enough for everything the portal
/// does, and nothing that could change the schema, switch off the audit trigger or rewrite the audit chain.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class RuntimeRoleTests(PortalDatabaseFixture database) : IClassFixture<PortalDatabaseFixture>
{
    private const int PermissionDenied = 229;

    // After the seed, so a reset has months to file.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2027, 1, 15, 9, 0, 0, TimeSpan.Zero));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("UPDATE audit.AuditEntries SET Details = Details;")]
    [InlineData("DELETE FROM audit.AuditEntries;")]
    public async Task The_role_cannot_change_or_delete_an_audit_entry_even_before_the_trigger_runs(string statement)
    {
        var user = await RuntimeUserAsync();

        var error = await Should.ThrowAsync<SqlException>(() => ExecuteAsAsync(user, statement));

        error.Number.ShouldBe(PermissionDenied);
    }

    [Theory]
    [InlineData("CREATE TABLE returns.Probe (Id int NOT NULL);")]
    [InlineData("ALTER TABLE audit.AuditEntries DISABLE TRIGGER TR_AuditEntries_AppendOnly;")]
    [InlineData("DROP VIEW reporting.ObligationCompliance;")]
    public async Task The_role_cannot_change_the_schema_or_switch_off_the_trigger(string statement)
    {
        var user = await RuntimeUserAsync();

        await Should.ThrowAsync<SqlException>(() => ExecuteAsAsync(user, statement));
    }

    [Fact]
    public async Task The_role_reads_and_writes_data()
    {
        var user = await RuntimeUserAsync();

        await Should.NotThrowAsync(() => ExecuteAsAsync(user, """
            DECLARE @count int = (SELECT COUNT(*) FROM reference.Institutions);
            DECLARE @views int = (SELECT COUNT(*) FROM reporting.ObligationCompliance);
            UPDATE reference.Institutions SET Name = Name WHERE Code = N'HLB';
            DELETE FROM api.IdempotencyRecords WHERE 1 = 0;
            """));
    }

    [Fact]
    public async Task The_portal_runs_a_demo_reset_and_reports_as_a_login_in_the_role()
    {
        var connectionString = await RuntimeLoginAsync();
        await using var factory = PortalHost.Create(
            connectionString,
            services => services.AddSingleton<TimeProvider>(_time),
            new Dictionary<string, string?> { ["Demo:Enabled"] = "true" });

        // Users are linked with the administrator login, as the test setup elsewhere; the portal itself never is.
        var portal = new SupervisionPortal(factory, database.ConnectionString);
        using var admin = await portal.AdminAsync();
        var reset = await PortalForms.PostAsync(admin, PortalAreas.Admin.Path, PortalAreas.Admin.Path + "/demo/reset");
        (await PortalForms.FollowAsync(admin, reset)).ShouldContain("The demo was reset: ");

        using var reviewer = await portal.ReviewerAsync();
        (await reviewer.GetAsync(new Uri("/reports", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IAuditChainVerifier>().VerifyAsync(Ct)).IsIntact.ShouldBeTrue();
    }

    private async Task<string> RuntimeUserAsync()
    {
        var user = $"runtime_probe_{Guid.NewGuid():N}";
        await ExecuteAsync($"CREATE USER [{user}] WITHOUT LOGIN; ALTER ROLE [{DatabaseRoles.Runtime}] ADD MEMBER [{user}];");
        return user;
    }

    // As the production deployment does: a SQL login whose database user is a member of the role and nothing else.
    private async Task<string> RuntimeLoginAsync()
    {
        var login = $"runtime_{Guid.NewGuid():N}";
        var password = $"Rt-{Guid.NewGuid():N}!";
        await ExecuteAsync($"""
            CREATE LOGIN [{login}] WITH PASSWORD = N'{password}', CHECK_POLICY = OFF;
            CREATE USER [{login}] FOR LOGIN [{login}];
            ALTER ROLE [{DatabaseRoles.Runtime}] ADD MEMBER [{login}];
            """);
        return new SqlConnectionStringBuilder(database.ConnectionString) { UserID = login, Password = password }.ConnectionString;
    }

    private Task ExecuteAsAsync(string user, string statement) =>
        ExecuteAsync($"EXECUTE AS USER = N'{user}'; BEGIN TRY {statement} END TRY BEGIN CATCH REVERT; THROW; END CATCH; REVERT;");

    private async Task ExecuteAsync(string batch)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand(batch, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}
