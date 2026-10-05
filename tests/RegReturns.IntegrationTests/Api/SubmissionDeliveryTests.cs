using System.Net;

using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Identity;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Submissions;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Api;

/// <summary>
/// <c>POST /v1/submissions</c> (ADR 0026, 0027): bank systems deliver returns as drafts through their client user,
/// safely retried with idempotency keys. Each test works on its own obligation of the seeded current period.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class SubmissionDeliveryTests(TestSchemeApiFixture api) : IClassFixture<TestSchemeApiFixture>
{
    private const string Path = "/v1/submissions";

    // The seeded current periods (the seed date is 4 October 2026).
    private const string Month = "2026-09";
    private const string Quarter = "2026-Q3";

    [Fact]
    public async Task Delivering_a_return_starts_a_draft_prepared_by_the_client_user()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await api.Database.RegisterClientAsync(DemoBank.Harbourline, active: true, ct);
        using var client = api.Factory.ClientAs(clientId);

        var response = await client.PostWithKeyAsync(Path, Delivery(MlrTemplate.Code, Month, MlrValues()), NewKey(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = await response.ReadJsonAsync(ct);
        var root = body.RootElement;
        var id = root.GetProperty("submissionId").GetGuid();
        response.Headers.Location!.AbsolutePath.ShouldBe($"/v1/submissions/{id}");
        root.GetProperty("created").GetBoolean().ShouldBeTrue();
        root.GetProperty("status").GetString().ShouldBe("Draft");
        root.GetProperty("validation").GetProperty("isValidated").GetBoolean().ShouldBeTrue();

        await using var db = SqlServerFixture.CreateContext(api.Database.ConnectionString);
        var submission = await db.Submissions.AsNoTracking().Include(s => s.Values).SingleAsync(s => s.Id == id, ct);
        var clientUser = await db.Users.AsNoTracking()
            .SingleAsync(u => db.ApiClients.Any(c => c.Id == u.ApiClientId && c.Wso2ClientId == clientId), ct);
        submission.Source.ShouldBe(SubmissionSource.Api);
        submission.PreparedByUserId.ShouldBe(clientUser.Id);
        submission.FindValue(MlrTemplate.TotalHqla)!.RawValue.ShouldBe("5200000.50");
        submission.FindValue(MlrTemplate.Lcr)!.RawValue.ShouldBe("130");
    }

    [Fact]
    public async Task Delivery_is_audited_with_the_api_client_as_the_actor()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await api.Database.RegisterClientAsync(DemoBank.Meridian, active: true, ct);
        using var client = api.Factory.ClientAs(clientId);

        var response = await client.PostWithKeyAsync(Path, Delivery(QcarTemplate.Code, Quarter, []), NewKey(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (await api.Database.AuditEntriesAsync(response.TraceId(), ct))
            .Where(e => e.EntityType == nameof(Submission))
            .ShouldHaveSingleItem();
        created.Action.ShouldBe(AuditAction.Created);
        created.ActorType.ShouldBe(ActorType.ApiClient);
        created.ActorSubjectId.ShouldBe(clientId);
        created.InstitutionCode.ShouldBe(DemoBank.Meridian);
    }

    [Fact]
    public async Task Delivering_into_an_open_draft_replaces_every_value()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await api.Database.RegisterClientAsync(DemoBank.Harbourline, active: true, ct);
        using var client = api.Factory.ClientAs(clientId);
        var draftId = await LiveSubmissionIdAsync(DemoBank.Harbourline, MdaTemplate.Code, ct);

        var response = await client.PostWithKeyAsync(
            Path, Delivery(MdaTemplate.Code, Month, new() { [MdaTemplate.DepDemand] = 1000 }), NewKey(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Location.ShouldBeNull();
        using var body = await response.ReadJsonAsync(ct);
        body.RootElement.GetProperty("submissionId").GetGuid().ShouldBe(draftId);
        body.RootElement.GetProperty("created").GetBoolean().ShouldBeFalse();
        body.RootElement.GetProperty("changed").GetBoolean().ShouldBeTrue();

        await using var db = SqlServerFixture.CreateContext(api.Database.ConnectionString);
        var draft = await db.Submissions.AsNoTracking().Include(s => s.Values).SingleAsync(s => s.Id == draftId, ct);
        draft.FindValue(MdaTemplate.DepDemand)!.RawValue.ShouldBe("1000");
        draft.FindValue(MdaTemplate.DepSavings)!.RawValue.ShouldBeNull();
        draft.Values.Where(v => v.RawValue is not null).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Retry_with_the_same_key_replays_the_first_response_and_delivers_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await api.Database.RegisterClientAsync(DemoBank.LotusUnion, active: true, ct);
        using var client = api.Factory.ClientAs(clientId);
        var key = NewKey();
        var delivery = Delivery(MdaTemplate.Code, Month, new() { [MdaTemplate.DepDemand] = "2500.00" });

        var first = await client.PostWithKeyAsync(Path, delivery, key, ct);
        var retry = await client.PostWithKeyAsync(Path, delivery, key, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        first.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        retry.StatusCode.ShouldBe(HttpStatusCode.Created);
        retry.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        retry.Headers.Location.ShouldBe(first.Headers.Location);
        (await retry.Content.ReadAsStringAsync(ct)).ShouldBe(await first.Content.ReadAsStringAsync(ct));

        await using var db = SqlServerFixture.CreateContext(api.Database.ConnectionString);
        var lotus = await db.Institutions.SingleAsync(i => i.Code == DemoBank.LotusUnion, ct);
        (await db.Submissions.CountAsync(s => s.InstitutionId == lotus.Id && s.Source == SubmissionSource.Api, ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Same_key_with_another_body_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await api.Database.RegisterClientAsync(DemoBank.Northgate, active: true, ct);
        using var client = api.Factory.ClientAs(clientId);
        var key = NewKey();

        var first = await client.PostWithKeyAsync(Path, Delivery(QcarTemplate.Code, Quarter, new() { [QcarTemplate.Cet1] = 100 }), key, ct);
        var reused = await client.PostWithKeyAsync(Path, Delivery(QcarTemplate.Code, Quarter, new() { [QcarTemplate.Cet1] = 101 }), key, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        reused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await reused.ReadProblemCodeAsync(ct)).ShouldBe("Idempotency.KeyReused");
    }

    [Fact]
    public async Task Keys_belong_to_one_client()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = NewKey();
        using var lotus = api.Factory.ClientAs(await api.Database.RegisterClientAsync(DemoBank.LotusUnion, active: true, ct));
        using var crestmont = api.Factory.ClientAs(await api.Database.RegisterClientAsync(DemoBank.Crestmont, active: true, ct));

        var mine = await lotus.PostWithKeyAsync(Path, Delivery(MlrTemplate.Code, "2030-01", []), key, ct);
        var theirs = await crestmont.PostWithKeyAsync(Path, Delivery(MlrTemplate.Code, "2030-02", []), key, ct);

        (await mine.ReadProblemCodeAsync(ct)).ShouldBe("Delivery.NoObligation");
        (await theirs.ReadProblemCodeAsync(ct)).ShouldBe("Delivery.NoObligation");
        theirs.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
    }

    [Fact]
    public async Task Delivering_into_a_submitted_return_is_a_conflict_that_is_not_replayed()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await api.Database.RegisterClientAsync(DemoBank.Crestmont, active: true, ct);
        using var client = api.Factory.ClientAs(clientId);
        var key = NewKey();
        var delivery = Delivery(MdaTemplate.Code, Month, new() { [MdaTemplate.DepDemand] = 1 });

        var first = await client.PostWithKeyAsync(Path, delivery, key, ct);
        var retry = await client.PostWithKeyAsync(Path, delivery, key, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await first.ReadProblemCodeAsync(ct)).ShouldBe(SubmissionErrors.NotEditable.Code);
        retry.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        retry.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
    }

    [Theory]
    [InlineData(MlrTemplate.Code, "2030-01", "Delivery.NoObligation")]
    [InlineData(MlrTemplate.Code, Quarter, "ReturnType.PeriodMismatch")]
    [InlineData("NOPE", Month, "ReturnType.NotFound")]
    public async Task Delivery_for_a_return_the_bank_does_not_owe_is_refused(string returnType, string period, string code)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = api.Factory.ClientAs(await api.Database.RegisterClientAsync(DemoBank.Harbourline, active: true, ct));

        var response = await client.PostWithKeyAsync(Path, Delivery(returnType, period, []), NewKey(), ct);

        (await response.ReadProblemCodeAsync(ct)).ShouldBe(code);
        response.StatusCode.ShouldBe(code.EndsWith(".NotFound", StringComparison.Ordinal) ? HttpStatusCode.NotFound : HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Unknown_field_codes_are_listed_and_nothing_is_stored()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await api.Database.RegisterClientAsync(DemoBank.Meridian, active: true, ct);
        using var client = api.Factory.ClientAs(clientId);

        var response = await client.PostWithKeyAsync(
            Path, Delivery(MdaTemplate.Code, Month, new() { ["NOT_A_FIELD"] = 1, [MdaTemplate.DepDemand] = 2 }), NewKey(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var body = await response.ReadJsonAsync(ct);
        body.RootElement.GetProperty("code").GetString().ShouldBe("Delivery.UnknownFields");
        body.RootElement.GetProperty("detail").GetString()!.ShouldContain("NOT_A_FIELD");
        (await LiveSubmissionIdOrNullAsync(DemoBank.Meridian, MdaTemplate.Code, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_value_that_is_an_object_is_a_bad_request()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = api.Factory.ClientAs(await api.Database.RegisterClientAsync(DemoBank.Harbourline, active: true, ct));

        var response = await client.PostWithKeyAsync(
            Path, Delivery(MlrTemplate.Code, Month, new() { [MlrTemplate.Lcr] = new { value = 1 } }), NewKey(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var body = await response.ReadJsonAsync(ct);
        body.RootElement.GetProperty("code").GetString().ShouldBe("Request.Invalid");
        body.RootElement.GetProperty("errors").TryGetProperty($"values.{MlrTemplate.Lcr}", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task A_body_over_one_megabyte_is_refused_before_it_is_read()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = api.Factory.ClientAs(await api.Database.RegisterClientAsync(DemoBank.Harbourline, active: true, ct));
        var values = Enumerable.Range(0, 400).ToDictionary(i => $"F{i}", object? (_) => new string('9', 3000));

        var response = await client.PostWithKeyAsync(Path, Delivery(MlrTemplate.Code, Month, values), NewKey(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await response.ReadProblemCodeAsync(ct)).ShouldBe("Request.TooLarge");
    }

    [Theory]
    [InlineData(null, "Idempotency.KeyRequired")]
    [InlineData("has space", "Idempotency.KeyInvalid")]
    public async Task A_post_needs_a_valid_idempotency_key(string? key, string code)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = api.Factory.ClientAs(await api.Database.RegisterClientAsync(DemoBank.Harbourline, active: true, ct));

        var response = await client.PostWithKeyAsync(Path, Delivery(MlrTemplate.Code, Month, []), key, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync(ct)).ShouldBe(code);
    }

    [Fact]
    public async Task A_read_only_client_cannot_deliver()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await api.Database.RegisterClientAsync(DemoBank.Harbourline, active: true, ct);
        using var client = api.Factory.ClientAs(clientId, $"{ApiScopes.ReturnsRead} {ApiScopes.ReferenceRead}");

        var response = await client.PostWithKeyAsync(Path, Delivery(MlrTemplate.Code, Month, []), NewKey(), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_client_without_a_client_user_is_forbidden_and_its_key_stays_free()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await api.Database.RegisterClientAsync(DemoBank.Harbourline, active: true, ct, withClientUser: false);
        using var client = api.Factory.ClientAs(clientId);
        var key = NewKey();

        var refused = await client.PostWithKeyAsync(Path, Delivery(QcarTemplate.Code, Quarter, []), key, ct);
        await using (var db = SqlServerFixture.CreateContext(api.Database.ConnectionString))
        {
            await db.Users.AddAsync(AppUser.ForApiClient(await db.ApiClients.SingleAsync(c => c.Wso2ClientId == clientId, ct)), ct);
            await db.SaveChangesAsync(ct);
        }

        var retried = await client.PostWithKeyAsync(Path, Delivery(QcarTemplate.Code, Quarter, []), key, ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.ReadProblemCodeAsync(ct)).ShouldBe(ActorErrors.NotLinked.Code);
        retried.StatusCode.ShouldBe(HttpStatusCode.Created);
        retried.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
    }

    private static object Delivery(string returnType, string period, Dictionary<string, object?> values) =>
        new { returnType, period, values };

    private static Dictionary<string, object?> MlrValues() => new()
    {
        [MlrTemplate.TotalHqla] = 5200000.50m,
        [MlrTemplate.Lcr] = "130",
        [MlrTemplate.L1Hqla] = null,
    };

    private static string NewKey() => Guid.NewGuid().ToString();

    // Monthly returns only: the live submission for September 2026.
    private async Task<Guid> LiveSubmissionIdAsync(string bank, string returnType, CancellationToken ct) =>
        await LiveSubmissionIdOrNullAsync(bank, returnType, ct) ?? throw new InvalidOperationException("No live submission.");

    private async Task<Guid?> LiveSubmissionIdOrNullAsync(string bank, string returnType, CancellationToken ct)
    {
        await using var db = SqlServerFixture.CreateContext(api.Database.ConnectionString);
        return await (
            from s in db.Submissions
            join o in db.Obligations on s.ObligationId equals o.Id
            join r in db.ReturnTypes on s.ReturnTypeId equals r.Id
            join i in db.Institutions on s.InstitutionId equals i.Id
            where i.Code == bank && r.Code == returnType && s.Status != SubmissionStatus.Rejected
                && o.Period.Year == 2026 && o.Period.Number == 9
            select (Guid?)s.Id)
            .SingleOrDefaultAsync(ct);
    }
}
