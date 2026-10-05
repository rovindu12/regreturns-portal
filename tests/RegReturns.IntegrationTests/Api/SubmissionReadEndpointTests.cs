extern alias ApiHost;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ApiHost::RegReturns.Api.Contracts;

using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Identity;
using RegReturns.Domain.Submissions;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Api;

/// <summary>Reading a bank's returns over the API: lists, filters, details, findings and institution scoping.</summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class SubmissionReadEndpointTests(TestSchemeApiFixture api) : IClassFixture<TestSchemeApiFixture>
{
    private const string Path = "/v1/submissions";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task The_list_holds_every_return_of_the_bank_newest_first()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await HarbourlineClientAsync(ct);

        var page = await client.GetFromJsonAsync<PagedResponse<SubmissionResponse>>($"{Path}?pageSize=100", Json, ct);

        page.ShouldNotBeNull();
        page.TotalCount.ShouldBe(await CountAsync(DemoBank.Harbourline, ct));
        page.Items.Count.ShouldBe(Math.Min(page.TotalCount, 100));
        page.Items.Select(i => i.CreatedAt).ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    public async Task A_page_links_to_the_first_previous_next_and_last_pages()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await HarbourlineClientAsync(ct);

        var response = await client.GetAsync($"{Path}?page=2&pageSize=5&returnType=MLR", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<PagedResponse<SubmissionResponse>>(Json, ct);
        page!.Page.ShouldBe(2);
        page.Items.Count.ShouldBe(5);
        var last = page.TotalPages;
        last.ShouldBeGreaterThan(2);
        var link = string.Join(", ", response.Headers.GetValues("Link"));
        link.ShouldBe(string.Join(", ", Link(1, "first"), Link(1, "prev"), Link(3, "next"), Link(last, "last")));

        // Other parameters keep their place; page and pageSize go last.
        static string Link(int target, string rel) => $"<{Path}?returnType=MLR&page={target}&pageSize=5>; rel=\"{rel}\"";
    }

    [Fact]
    public async Task Filters_narrow_the_list_to_the_open_draft()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await HarbourlineClientAsync(ct);

        var page = await client.GetFromJsonAsync<PagedResponse<SubmissionResponse>>(
            $"{Path}?status=draft&returnType={MdaTemplate.Code}&period=2026-09", Json, ct);

        var draft = page!.Items.ShouldHaveSingleItem();
        draft.ReturnType.ShouldBe(MdaTemplate.Code);
        draft.Period.ShouldBe("2026-09");
        draft.Status.ShouldBe(nameof(SubmissionStatus.Draft));
        page.TotalCount.ShouldBe(1);
    }

    [Theory]
    [InlineData("status=1")]
    [InlineData("status=Pending")]
    [InlineData("pageSize=101")]
    [InlineData("page=0")]
    [InlineData("period=2026-13")]
    [InlineData("returnType=no%20such")]
    public async Task A_bad_query_is_an_invalid_request(string query)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await HarbourlineClientAsync(ct);

        var response = await client.GetAsync($"{Path}?{query}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemCodeAsync(ct)).ShouldBe("Request.Invalid");
    }

    [Fact]
    public async Task A_return_comes_with_a_value_for_every_field_and_its_history()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await HarbourlineClientAsync(ct);
        var (id, fieldCount) = await SubmissionAsync(DemoBank.Harbourline, MdaTemplate.Code, 2026, 7, ct);

        var detail = await client.GetFromJsonAsync<SubmissionDetailResponse>($"{Path}/{id}", Json, ct);

        detail.ShouldNotBeNull();
        detail.Submission.Id.ShouldBe(id);
        detail.Submission.Status.ShouldBe(nameof(SubmissionStatus.Approved));
        detail.Values.Count.ShouldBe(fieldCount);
        detail.Values[MdaTemplate.NplRatio].ShouldNotBeNullOrWhiteSpace();
        detail.History.Select(h => h.Action).ShouldBe(
            [nameof(WorkflowAction.Create), nameof(WorkflowAction.Submit), nameof(WorkflowAction.StartReview), nameof(WorkflowAction.Approve)]);
        detail.History[0].FromStatus.ShouldBeNull();
    }

    [Fact]
    public async Task Findings_carry_the_banks_justifications()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await HarbourlineClientAsync(ct);
        var (id, _) = await SubmissionAsync(DemoBank.Harbourline, MdaTemplate.Code, 2026, 7, ct);

        var validation = await client.GetFromJsonAsync<ValidationResponse>($"{Path}/{id}/validation", Json, ct);

        validation.ShouldNotBeNull();
        validation.IsValidated.ShouldBeTrue();
        validation.ReadyToSubmit.ShouldBeFalse();
        validation.UnjustifiedWarnings.ShouldBe(0);
        var npl = validation.Findings.Single(f => f.Rule == MdaTemplate.RuleNplMaximum);
        npl.Severity.ShouldBe("Warning");
        npl.Justification!.ShouldStartWith("Two large manufacturing exposures");
    }

    [Theory]
    [InlineData("")]
    [InlineData("/validation")]
    public async Task Another_banks_return_is_not_found_like_an_unknown_id(string suffix)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await HarbourlineClientAsync(ct);
        var (otherBanks, _) = await SubmissionAsync(DemoBank.Crestmont, MdaTemplate.Code, 2026, 9, ct);

        var otherBank = await client.GetAsync($"{Path}/{otherBanks}{suffix}", ct);
        var unknown = await client.GetAsync($"{Path}/{Guid.CreateVersion7()}{suffix}", ct);

        otherBank.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await otherBank.ReadProblemCodeAsync(ct)).ShouldBe(SubmissionErrors.NotFound.Code);
        (await ProblemWithoutRequestMembersAsync(otherBank, ct)).ShouldBe(await ProblemWithoutRequestMembersAsync(unknown, ct));
    }

    [Fact]
    public async Task A_client_without_the_read_scope_is_forbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await api.Database.RegisterClientAsync(DemoBank.Harbourline, active: true, ct);
        using var client = api.Factory.ClientAs(clientId, ApiScopes.ReferenceRead);

        var response = await client.GetAsync(Path, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> HarbourlineClientAsync(CancellationToken ct) =>
        api.Factory.ClientAs(await api.Database.RegisterClientAsync(DemoBank.Harbourline, active: true, ct));

    private async Task<int> CountAsync(string bank, CancellationToken ct)
    {
        await using var db = SqlServerFixture.CreateContext(api.Database.ConnectionString);
        return await db.Submissions.CountAsync(
            s => db.Institutions.Any(i => i.Id == s.InstitutionId && i.Code == bank), ct);
    }

    // The live submission of a monthly return and the number of fields of its template.
    private async Task<(Guid Id, int FieldCount)> SubmissionAsync(string bank, string returnType, int year, int month, CancellationToken ct)
    {
        await using var db = SqlServerFixture.CreateContext(api.Database.ConnectionString);
        var row = await (
            from s in db.Submissions
            join o in db.Obligations on s.ObligationId equals o.Id
            join r in db.ReturnTypes on s.ReturnTypeId equals r.Id
            join i in db.Institutions on s.InstitutionId equals i.Id
            where i.Code == bank && r.Code == returnType && s.Status != SubmissionStatus.Rejected
                && o.Period.Year == year && o.Period.Number == month
            select new { s.Id, s.TemplateVersionId })
            .SingleAsync(ct);
        var fields = await db.TemplateVersions.Where(v => v.Id == row.TemplateVersionId).SelectMany(v => v.Fields).CountAsync(ct);
        return (row.Id, fields);
    }

    // A problem body without the members that legitimately differ per request.
    private static async Task<string> ProblemWithoutRequestMembersAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var json = await response.ReadJsonAsync(ct);
        return string.Join('\n', json.RootElement.EnumerateObject()
            .Where(p => p.Name is not ("traceId" or "instance"))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value.GetRawText()}"));
    }
}
