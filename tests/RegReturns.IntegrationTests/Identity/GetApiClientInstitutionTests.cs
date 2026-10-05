using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Application.Identity;
using RegReturns.Domain.Institutions;
using RegReturns.Infrastructure.Persistence;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Identity;

public sealed class GetApiClientInstitutionTests(SqlServerFixture sql)
{
    private const string ClientId = "Xq7_alphaCoreBanking";

    [Fact]
    public async Task Active_client_of_an_active_institution_returns_that_institution()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, alpha) = await CreateDatabaseAsync(ct);

        var found = await QueryAsync(connection, ClientId, ct);

        found.ShouldBe(new ApiClientInstitution(alpha.Id, "ALPHA"));
    }

    [Fact]
    public async Task Unknown_client_returns_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, _) = await CreateDatabaseAsync(ct);

        (await QueryAsync(connection, "not-registered", ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Client_id_in_a_different_case_returns_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, _) = await CreateDatabaseAsync(ct);

        (await QueryAsync(connection, ClientId.ToUpperInvariant(), ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Deactivated_client_returns_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, _) = await CreateDatabaseAsync(ct, deactivateClient: true);

        (await QueryAsync(connection, ClientId, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Client_of_a_deactivated_institution_returns_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, _) = await CreateDatabaseAsync(ct, deactivateInstitution: true);

        (await QueryAsync(connection, ClientId, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Each_client_maps_only_to_its_own_institution()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, _) = await CreateDatabaseAsync(ct);

        (await QueryAsync(connection, "beta-treasury", ct))!.InstitutionCode.ShouldBe("BETA");
    }

    private static async Task<ApiClientInstitution?> QueryAsync(string connection, string clientId, CancellationToken ct)
    {
        await using var db = SqlServerFixture.CreateContext(connection);
        return await new GetApiClientInstitutionHandler(db).HandleAsync(new GetApiClientInstitution(clientId), ct);
    }

    private async Task<(string Connection, Institution Alpha)> CreateDatabaseAsync(
        CancellationToken ct, bool deactivateClient = false, bool deactivateInstitution = false)
    {
        var connection = sql.NewDatabaseConnectionString();
        await using var db = SqlServerFixture.CreateContext(connection);
        await new DatabaseInitializer(db, new FakeTimeProvider(SqlServerFixture.SeedDate), NullLogger<DatabaseInitializer>.Instance)
            .MigrateAsync(ct);

        var alpha = Institution.Create("ALPHA", "Alpha Bank of Valoria PLC", LicenceCategory.Commercial);
        var beta = Institution.Create("BETA", "Beta Savings Bank", LicenceCategory.Savings);
        var alphaClient = ApiClient.Create(alpha, ClientId, "Alpha core banking");
        if (deactivateClient)
        {
            alphaClient.Deactivate();
        }

        if (deactivateInstitution)
        {
            alpha.Deactivate();
        }

        await db.Institutions.AddRangeAsync([alpha, beta], ct);
        await db.ApiClients.AddRangeAsync([alphaClient, ApiClient.Create(beta, "beta-treasury", "Beta treasury")], ct);
        await db.SaveChangesAsync(ct);
        return (connection, alpha);
    }
}
