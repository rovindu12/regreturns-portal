extern alias ApiHost;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ApiHost::RegReturns.Api.Contracts;

using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Hosting;

namespace RegReturns.IntegrationTests.Api;

/// <summary>The reference endpoints a bank's system reads before it delivers a return.</summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class ReturnTypeEndpointTests(TestSchemeApiFixture api) : IClassFixture<TestSchemeApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task The_list_holds_every_return_type_with_its_published_versions()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await ClientAsync(ct);

        var types = await client.GetFromJsonAsync<List<ReturnTypeResponse>>("/v1/return-types", Json, ct);

        types.ShouldNotBeNull();
        types.Select(t => t.Code).ShouldBe([MdaTemplate.Code, MlrTemplate.Code, QcarTemplate.Code], ignoreOrder: true);
        var qcar = types.Single(t => t.Code == QcarTemplate.Code);
        qcar.Frequency.ShouldBe("Quarterly");
        qcar.Versions.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task The_template_lists_the_field_codes_and_rules_of_the_period()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await ClientAsync(ct);

        var template = await client.GetFromJsonAsync<TemplateResponse>($"/v1/return-types/{MlrTemplate.Code}/template?period=2026-09", Json, ct);

        template.ShouldNotBeNull();
        template.ReturnType.ShouldBe(MlrTemplate.Code);
        template.Period.ShouldBe("2026-09");
        template.Fields.Select(f => f.Code).ShouldContain(MlrTemplate.TotalHqla);
        template.Fields.Single(f => f.Code == MlrTemplate.Lcr).DataType.ShouldBe("Percentage");
        template.Rules.Select(r => r.Code).ShouldContain(MlrTemplate.RuleLcrMinimum);
    }

    [Fact]
    public async Task Without_a_period_the_template_is_the_one_in_force_today()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await ClientAsync(ct);

        var template = await client.GetFromJsonAsync<TemplateResponse>($"/v1/return-types/{QcarTemplate.Code}/template", Json, ct);

        template!.Period.ShouldMatch("^[0-9]{4}-Q[1-4]$");
    }

    [Theory]
    [InlineData("MLR", "period=2026-Q3", HttpStatusCode.UnprocessableEntity, "ReturnType.PeriodMismatch")]
    [InlineData("NOPE", "", HttpStatusCode.NotFound, "ReturnType.NotFound")]
    [InlineData("MLR", "period=2026-9", HttpStatusCode.BadRequest, "Request.Invalid")]
    public async Task A_template_that_cannot_be_given_is_a_problem(string code, string query, HttpStatusCode status, string error)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await ClientAsync(ct);

        var response = await client.GetAsync($"/v1/return-types/{code}/template?{query}", ct);

        response.StatusCode.ShouldBe(status);
        (await response.ReadProblemCodeAsync(ct)).ShouldBe(error);
    }

    private async Task<HttpClient> ClientAsync(CancellationToken ct) =>
        api.Factory.ClientAs(await api.Database.RegisterClientAsync(DemoBank.Northgate, active: true, ct));
}
