using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Hosting;

namespace RegReturns.IntegrationTests.Web;

[Collection(HostedAppsDefinition.Name)]
public sealed class BankReturnPagesTests : IClassFixture<PortalDatabaseFixture>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly BankPortal _portal;

    public BankReturnPagesTests(PortalDatabaseFixture database)
    {
        _factory = PortalHost.Create(database.ConnectionString);
        _portal = new BankPortal(_factory, database.ConnectionString);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Overview_lists_the_bank_obligations_and_offers_a_maker_to_start_an_open_one()
    {
        var obligationId = await _portal.NewMlrObligationAsync(1);
        using var client = await _portal.MakerAsync();

        var html = await client.GetStringAsync("/bank", Ct);

        html.ShouldContain("Harbourline Bank PLC (HLB)");
        html.ShouldContain("2027-01");
        html.ShouldContain($"action=\"/bank/obligations/{obligationId}/start\"");
        html.ShouldContain($"href=\"/bank/obligations/{obligationId}/upload\"");
    }

    [Fact]
    public async Task Overview_offers_a_checker_no_way_to_start_or_upload()
    {
        var obligationId = await _portal.NewMlrObligationAsync(2);
        using var client = await _portal.CheckerAsync();

        var html = await client.GetStringAsync("/bank", Ct);

        html.ShouldContain("2027-02");
        html.ShouldNotContain($"/bank/obligations/{obligationId}/start");
        html.ShouldNotContain($"/bank/obligations/{obligationId}/upload");
    }

    [Fact]
    public async Task Overview_explains_when_the_sign_in_is_not_linked_to_a_portal_user()
    {
        using var client = _portal.UnlinkedMaker();

        var html = await client.GetStringAsync("/bank", Ct);

        html.ShouldContain("not linked to an active RegReturns user");
    }

    [Fact]
    public async Task Maker_starts_a_draft_saves_values_and_sees_them_when_the_return_is_reopened()
    {
        var obligationId = await _portal.NewMlrObligationAsync(3);
        using var client = await _portal.MakerAsync();
        var submissionId = await BankPortal.StartDraftAsync(client, obligationId);

        var response = await BankPortal.SaveAsync(client, submissionId, BankPortal.ValidMlr);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var html = await client.GetStringAsync($"/bank/returns/{submissionId}", Ct);
        html.ShouldContain("Draft saved. Validation found no problems.");
        html.ShouldContain($"name=\"Values[{MlrTemplate.Lcr}]\" type=\"text\" class=\"form-control\" value=\"216.67\"");
        var submission = await _portal.SubmissionAsync(submissionId);
        submission.Status.ShouldBe(SubmissionStatus.Draft);
        submission.FindValue(MlrTemplate.TotalHqla)!.NumericValue.ShouldBe(1300m);
        submission.ValidatedEditVersion.ShouldBe(submission.EditVersion);
    }

    [Fact]
    public async Task Starting_a_return_twice_opens_the_draft_already_started()
    {
        var obligationId = await _portal.NewMlrObligationAsync(4);
        using var client = await _portal.MakerAsync();

        var first = await BankPortal.StartDraftAsync(client, obligationId);
        var second = await BankPortal.StartDraftAsync(client, obligationId);

        second.ShouldBe(first);
    }

    [Fact]
    public async Task Saving_inconsistent_figures_shows_the_errors_next_to_the_fields()
    {
        var obligationId = await _portal.NewMlrObligationAsync(5);
        using var client = await _portal.MakerAsync();
        var submissionId = await BankPortal.StartDraftAsync(client, obligationId);
        var values = new Dictionary<string, string?>(BankPortal.ValidMlr) { [MlrTemplate.TotalHqla] = "1400" };

        await BankPortal.SaveAsync(client, submissionId, values);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/bank/returns/{submissionId}", Ct));
        html.ShouldContain("Validation found 2 errors.");
        html.ShouldContain("Total HQLA must equal Level 1 + Level 2A + Level 2B. Entered 1,400; calculated 1,300.");
        html.ShouldContain($"id=\"field-{MlrTemplate.TotalHqla}\" name=\"Values[{MlrTemplate.TotalHqla}]\" type=\"text\" class=\"form-control is-invalid\"");
    }

    [Fact]
    public async Task Saving_with_a_stale_edit_version_keeps_the_entries_and_explains_the_conflict()
    {
        var obligationId = await _portal.NewMlrObligationAsync(6);
        using var client = await _portal.MakerAsync();
        var submissionId = await BankPortal.StartDraftAsync(client, obligationId);
        await BankPortal.SaveAsync(client, submissionId, BankPortal.ValidMlr);
        var stale = new Dictionary<string, string?>(BankPortal.ValidMlr) { [MlrTemplate.TotalDeposits] = "7777" };

        var response = await BankPortal.SaveAsync(client, submissionId, stale, editVersion: 0);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync(Ct);
        html.ShouldContain("Someone else saved this return after you opened it");
        html.ShouldContain("value=\"7777\"");
        (await _portal.SubmissionAsync(submissionId)).FindValue(MlrTemplate.TotalDeposits)!.RawValue.ShouldBe("5000");
    }

    [Fact]
    public async Task A_checker_cannot_save_values()
    {
        var obligationId = await _portal.NewMlrObligationAsync(7);
        using var maker = await _portal.MakerAsync();
        var submissionId = await BankPortal.StartDraftAsync(maker, obligationId);
        using var checker = await _portal.CheckerAsync();

        var token = await BankPortal.TokenFromAsync(checker, $"/bank/returns/{submissionId}");
        var response = await checker.PostAsync(
            $"/bank/returns/{submissionId}",
            BankPortal.Form(token, [new("EditVersion", "0"), new($"Values[{MlrTemplate.L1Hqla}]", "1")]),
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_warning_is_justified_once_the_explanation_is_long_enough()
    {
        var obligationId = await _portal.NewMlrObligationAsync(8);
        using var client = await _portal.MakerAsync();
        var submissionId = await BankPortal.StartDraftAsync(client, obligationId);
        var values = new Dictionary<string, string?>(BankPortal.ValidMlr)
        {
            [MlrTemplate.L1Hqla] = "900",
            [MlrTemplate.L2aHqla] = "100",
            [MlrTemplate.L2bHqla] = "300",
        };
        await BankPortal.SaveAsync(client, submissionId, values);
        var warning = (await _portal.SubmissionAsync(submissionId)).CurrentFindings.Single();
        warning.RuleCode.ShouldBe(MlrTemplate.RuleL2bCap);
        var path = $"/bank/returns/{submissionId}/findings/{warning.Id}/justify";

        var tooShort = await client.PostAsync(
            path, BankPortal.Form(await BankPortal.TokenFromAsync(client, $"/bank/returns/{submissionId}"), [new("justification", "Seems fine.")]), Ct);
        var accepted = await client.PostAsync(
            path,
            BankPortal.Form(
                await BankPortal.TokenFromAsync(client, $"/bank/returns/{submissionId}"),
                [new("justification", "Covered bonds bought this month; the cap breach is temporary and approved by ALCO.")]),
            Ct);

        (await tooShort.Content.ReadAsStringAsync(Ct)).ShouldContain("A justification must be between 20 and 2000 characters.");
        accepted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var justified = (await _portal.SubmissionAsync(submissionId)).CurrentFindings.Single();
        justified.Justification.ShouldStartWith("Covered bonds");
        justified.BlocksSubmission.ShouldBeFalse();
    }

    [Fact]
    public async Task Variance_rules_compare_with_the_last_approved_return_of_the_previous_month()
    {
        var obligationId = await _portal.CurrentMlrObligationAsync();
        using var client = await _portal.MakerAsync();
        var submissionId = await BankPortal.StartDraftAsync(client, obligationId);

        await BankPortal.SaveAsync(client, submissionId, BankPortal.ValidMlr);

        var findings = (await _portal.SubmissionAsync(submissionId)).CurrentFindings.ToList();
        var hqla = findings.Single(f => f.RuleCode == MlrTemplate.RuleHqlaVariance);
        hqla.Severity.ShouldBe(Severity.Warning);
        hqla.Message.ShouldContain("against the previous period (limit 30%)");
        findings.ShouldAllBe(f => f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task A_checker_validates_the_return_again()
    {
        var obligationId = await _portal.NewMlrObligationAsync(9);
        using var maker = await _portal.MakerAsync();
        var submissionId = await BankPortal.StartDraftAsync(maker, obligationId);
        await BankPortal.SaveAsync(maker, submissionId, BankPortal.ValidMlr);
        using var checker = await _portal.CheckerAsync();

        var token = await BankPortal.TokenFromAsync(checker, $"/bank/returns/{submissionId}");
        var response = await checker.PostAsync($"/bank/returns/{submissionId}/validate", BankPortal.Form(token), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await checker.GetStringAsync(response.Headers.Location!.OriginalString, Ct)).ShouldContain("Validation found no problems.");
    }

    [Fact]
    public async Task Another_bank_cannot_open_or_change_the_return()
    {
        var obligationId = await _portal.NewMlrObligationAsync(10);
        using var maker = await _portal.MakerAsync();
        var submissionId = await BankPortal.StartDraftAsync(maker, obligationId);
        using var other = await _portal.OtherBankMakerAsync();

        var page = await other.GetAsync($"/bank/returns/{submissionId}", Ct);
        var token = await BankPortal.TokenFromAsync(other, "/bank");
        var start = await other.PostAsync($"/bank/obligations/{obligationId}/start", BankPortal.Form(token), Ct);
        var download = await other.GetAsync($"/bank/obligations/{obligationId}/download?format=csv", Ct);

        page.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        start.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        download.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_approved_return_is_shown_read_only()
    {
        var submissionId = await _portal.HarbourlineSubmissionAsync(SubmissionStatus.Approved);
        using var client = await _portal.MakerAsync();

        var html = await client.GetStringAsync($"/bank/returns/{submissionId}", Ct);

        html.ShouldContain("can no longer be changed");
        html.ShouldNotContain("name=\"EditVersion\"");
    }

    public void Dispose() => _factory.Dispose();
}
