using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Domain.Identity;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// The submission workflow through the portal: a checker submits, a reviewer picks the return up, and an approver who
/// did not review it decides, with the segregation-of-duties and TOTP rules enforced on the way. The clock is fixed at
/// 1 March 2027, so a January 2027 return is late and later months are not.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class WorkflowPagesTests : IClassFixture<PortalDatabaseFixture>, IDisposable
{
    private static readonly DateTimeOffset Today = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Today) { AutoAdvanceAmount = TimeSpan.FromSeconds(1) };
    private readonly WebApplicationFactory<Program> _factory;
    private readonly BankPortal _bank;
    private readonly SupervisionPortal _supervision;
    private readonly string _connectionString;

    public WorkflowPagesTests(PortalDatabaseFixture database)
    {
        _connectionString = database.ConnectionString;
        _factory = PortalHost.Create(database.ConnectionString, services => services.AddSingleton<TimeProvider>(_clock));
        _bank = new BankPortal(_factory, database.ConnectionString);
        _supervision = new SupervisionPortal(_factory, database.ConnectionString);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Checker_submits_a_validated_return_with_a_comment()
    {
        var (_, id) = await DraftAsync(3);
        using var checker = await _bank.CheckerAsync();
        (await PortalForms.GetHtmlAsync(checker, BankPage(id))).ShouldContain($"action=\"{BankPage(id)}/submit\"");

        var response = await SupervisionPortal.PostStepAsync(checker, BankPage(id), $"{BankPage(id)}/submit", "Checked against the general ledger.");

        var page = await PortalForms.FollowAsync(checker, response);
        page.ShouldContain("Return submitted to the Bank of Valoria.");
        page.ShouldContain("Checked against the general ledger.");
        (await PortalForms.GetHtmlAsync(checker, BankPage(id))).ShouldNotContain($"action=\"{BankPage(id)}/submit\"");
        var submission = await _bank.SubmissionAsync(id);
        submission.Status.ShouldBe(SubmissionStatus.Submitted);
        submission.IsLate.ShouldBeFalse();
    }

    [Fact]
    public async Task Submitting_without_a_comment_shows_why_and_keeps_the_draft()
    {
        var (_, id) = await DraftAsync(2);
        using var checker = await _bank.CheckerAsync();

        var response = await SupervisionPortal.PostStepAsync(checker, BankPage(id), $"{BankPage(id)}/submit", "   ");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(Ct)).ShouldContain(SubmissionErrors.CommentRequired.Message);
        (await _bank.SubmissionAsync(id)).Status.ShouldBe(SubmissionStatus.Draft);
    }

    [Fact]
    public async Task Maker_is_refused_the_submit_step()
    {
        var (_, id) = await DraftAsync(4);
        using var maker = await _bank.MakerAsync();

        var response = await SupervisionPortal.PostStepAsync(maker, BankPage(id), $"{BankPage(id)}/submit", "Submitting my own work.");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _bank.SubmissionAsync(id)).Status.ShouldBe(SubmissionStatus.Draft);
    }

    [Fact]
    public async Task Checker_who_last_edited_the_values_must_leave_submission_to_another_checker()
    {
        var (_, id) = await DraftAsync(5);
        using var makerChecker = await _supervision.MultiRoleUserAsync("maker.checker.hlb", [Role.BankMaker, Role.BankChecker], DemoBank.Harbourline);
        var edited = new Dictionary<string, string?>(BankPortal.ValidMlr, StringComparer.Ordinal) { [MlrTemplate.L1Hqla] = "1000.00" };
        (await BankPortal.SaveAsync(makerChecker, id, edited)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var page = await PortalForms.GetTextAsync(makerChecker, BankPage(id));
        var refused = await SupervisionPortal.PostStepAsync(makerChecker, BankPage(id), $"{BankPage(id)}/submit", "Checked my own figures.");

        page.ShouldContain(SubmissionErrors.CheckerIsMaker.Message);
        refused.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync(Ct)).ShouldContain(SubmissionErrors.CheckerIsMaker.Message);
        using var checker = await _bank.CheckerAsync();
        var accepted = await SupervisionPortal.PostStepAsync(checker, BankPage(id), $"{BankPage(id)}/submit", "Independently checked.");
        PortalForms.RedirectPath(accepted).ShouldBe(BankPage(id));
    }

    [Fact]
    public async Task Return_goes_from_submission_through_review_to_approval_and_fulfils_the_obligation()
    {
        var (obligationId, id) = await SubmittedAsync(6);
        using var reviewer = await _supervision.ReviewerAsync();
        (await PortalForms.GetHtmlAsync(reviewer, "/supervision")).ShouldContain($"href=\"{SupervisionPage(id)}\"");

        var started = await SupervisionPortal.PostStepAsync(reviewer, SupervisionPage(id), $"{SupervisionPage(id)}/start-review");
        (await PortalForms.FollowAsync(reviewer, started)).ShouldContain("You are now reviewing this return.");
        using var approver = await _supervision.ApproverAsync();
        var approved = await SupervisionPortal.PostStepAsync(
            approver, SupervisionPage(id), $"{SupervisionPage(id)}/approve", "Figures reconcile with the prudential data.");

        (await PortalForms.FollowAsync(approver, approved)).ShouldContain("Return approved.");
        (await _bank.SubmissionAsync(id)).Status.ShouldBe(SubmissionStatus.Approved);
        (await _supervision.ObligationStatusAsync(obligationId)).ShouldBe(ObligationStatus.Fulfilled);
        using var checker = await _bank.CheckerAsync();
        var bankView = await PortalForms.GetTextAsync(checker, BankPage(id));
        bankView.ShouldContain("Figures reconcile with the prudential data.");
        bankView.ShouldContain("Review started");
        bankView.ShouldContain("The Bank of Valoria approved this return");
    }

    [Fact]
    public async Task Returned_return_shows_the_bank_why_and_comes_back_as_revision_two()
    {
        var (_, id) = await UnderReviewAsync(7);
        using var reviewer = await _supervision.ReviewerAsync();
        var sent = await SupervisionPortal.PostStepAsync(
            reviewer, SupervisionPage(id), $"{SupervisionPage(id)}/return-for-correction", "Liquid assets do not match the balance sheet.");
        PortalForms.RedirectPath(sent).ShouldBe(SupervisionPage(id));

        using var maker = await _bank.MakerAsync();
        var bankView = await PortalForms.GetTextAsync(maker, BankPage(id));
        (await BankPortal.SaveAsync(maker, id, BankPortal.ValidMlr)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        using var checker = await _bank.CheckerAsync();
        var resubmitted = await SupervisionPortal.PostStepAsync(checker, BankPage(id), $"{BankPage(id)}/submit", "Liquid assets corrected.");

        bankView.ShouldContain("Returned for correction by Supervisor Reviewer");
        bankView.ShouldContain("Liquid assets do not match the balance sheet.");
        PortalForms.RedirectPath(resubmitted).ShouldBe(BankPage(id));
        var submission = await _bank.SubmissionAsync(id);
        submission.Status.ShouldBe(SubmissionStatus.Submitted);
        submission.Revision.ShouldBe(2);
        (await PortalForms.GetTextAsync(reviewer, "/supervision")).ShouldContain("Revision 2");
    }

    [Fact]
    public async Task Approver_without_a_totp_sign_in_is_not_offered_the_decision_and_is_refused_it()
    {
        var (_, id) = await UnderReviewAsync(8);
        using var approver = await _supervision.ApproverAsync(withTotp: false);

        var page = await PortalForms.GetHtmlAsync(approver, SupervisionPage(id));
        var response = await SupervisionPortal.PostStepAsync(approver, SupervisionPage(id), $"{SupervisionPage(id)}/approve", "Looks right to me.");

        page.ShouldContain("needs a sign-in with your authenticator app");
        page.ShouldNotContain($"formaction=\"{SupervisionPage(id)}/approve\"");
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _bank.SubmissionAsync(id)).Status.ShouldBe(SubmissionStatus.UnderReview);
    }

    [Fact]
    public async Task Approver_who_reviewed_the_return_cannot_decide_it()
    {
        var (_, id) = await SubmittedAsync(9);
        using var both = await _supervision.MultiRoleUserAsync(
            "reviewer.approver", [Role.SupervisorReviewer, Role.SupervisorApprover], bankCode: null, withTotp: true);
        PortalForms.RedirectPath(
            await SupervisionPortal.PostStepAsync(both, SupervisionPage(id), $"{SupervisionPage(id)}/start-review")).ShouldBe(SupervisionPage(id));

        var page = await PortalForms.GetHtmlAsync(both, SupervisionPage(id));
        var refused = await SupervisionPortal.PostStepAsync(both, SupervisionPage(id), $"{SupervisionPage(id)}/approve", "Approving my own review.");

        page.ShouldNotContain($"formaction=\"{SupervisionPage(id)}/approve\"");
        refused.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync(Ct)).ShouldContain(SubmissionErrors.ApproverIsReviewer.Message);
        using var approver = await _supervision.ApproverAsync();
        var approved = await SupervisionPortal.PostStepAsync(approver, SupervisionPage(id), $"{SupervisionPage(id)}/approve", "Independent approval.");
        PortalForms.RedirectPath(approved).ShouldBe(SupervisionPage(id));
    }

    [Fact]
    public async Task Rejection_reopens_the_obligation_so_the_bank_can_file_again()
    {
        var (obligationId, id) = await UnderReviewAsync(10);
        using var approver = await _supervision.ApproverAsync();

        var rejected = await SupervisionPortal.PostStepAsync(
            approver, SupervisionPage(id), $"{SupervisionPage(id)}/reject", "The figures belong to another period.");

        PortalForms.RedirectPath(rejected).ShouldBe(SupervisionPage(id));
        (await _bank.SubmissionAsync(id)).Status.ShouldBe(SubmissionStatus.Rejected);
        (await _supervision.ObligationStatusAsync(obligationId)).ShouldBe(ObligationStatus.Open);
        using var maker = await _bank.MakerAsync();
        var bankView = await PortalForms.GetTextAsync(maker, BankPage(id));
        bankView.ShouldContain("Rejected by Supervisor Approver");
        bankView.ShouldContain("The figures belong to another period.");
        (await BankPortal.StartDraftAsync(maker, obligationId)).ShouldNotBe(id);
    }

    [Fact]
    public async Task Supervisors_never_see_a_draft_the_bank_has_not_submitted()
    {
        var (_, id) = await DraftAsync(11);
        using var reviewer = await _supervision.ReviewerAsync();

        var response = await reviewer.GetAsync(new Uri(SupervisionPage(id), UriKind.Relative), Ct);
        var worklist = await PortalForms.GetHtmlAsync(reviewer, "/supervision");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        worklist.ShouldNotContain(id.ToString());
    }

    [Fact]
    public async Task Another_banks_checker_cannot_submit_the_return()
    {
        var (_, id) = await DraftAsync(12);
        using var otherChecker = await _supervision.MultiRoleUserAsync(
            DemoUsers.CheckerUserName(DemoBank.Crestmont), [Role.BankChecker], DemoBank.Crestmont);

        var token = await BankPortal.TokenFromAsync(otherChecker, "/bank");
        var response = await otherChecker.PostAsync(
            new Uri($"{BankPage(id)}/submit", UriKind.Relative), BankPortal.Form(token, [new("comment", "Not my bank's return.")]), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _bank.SubmissionAsync(id)).Status.ShouldBe(SubmissionStatus.Draft);
    }

    [Fact]
    public async Task Late_submission_is_flagged_for_the_bank_and_in_the_late_filter()
    {
        var (_, lateId) = await DraftAsync(1);
        var (_, onTimeId) = await SubmittedAsync(1, year: 2028);
        using var checker = await _bank.CheckerAsync();
        (await PortalForms.GetTextAsync(checker, BankPage(lateId))).ShouldContain("so it will be marked late");

        var response = await SupervisionPortal.PostStepAsync(checker, BankPage(lateId), $"{BankPage(lateId)}/submit", "Filed late after a system outage.");

        (await PortalForms.FollowAsync(checker, response)).ShouldContain("it is marked late");
        (await _bank.SubmissionAsync(lateId)).IsLate.ShouldBeTrue();
        using var reviewer = await _supervision.ReviewerAsync();
        var lateOnly = await PortalForms.GetHtmlAsync(reviewer, "/supervision?late=true");
        lateOnly.ShouldContain($"href=\"{SupervisionPage(lateId)}\"");
        lateOnly.ShouldNotContain($"href=\"{SupervisionPage(onTimeId)}\"");
    }

    [Fact]
    public async Task Worklist_filters_by_bank()
    {
        var (_, id) = await SubmittedAsync(2, year: 2028);
        using var reviewer = await _supervision.ReviewerAsync();
        await using var context = SqlServerFixture.CreateContext(_connectionString);
        var banks = await context.Institutions
            .Where(i => i.Code == DemoBank.Harbourline || i.Code == DemoBank.Crestmont)
            .ToDictionaryAsync(i => i.Code, i => i.Id, Ct);

        var harbourline = await PortalForms.GetHtmlAsync(reviewer, $"/supervision?institution={banks[DemoBank.Harbourline]}");
        var crestmont = await PortalForms.GetHtmlAsync(reviewer, $"/supervision?institution={banks[DemoBank.Crestmont]}");

        harbourline.ShouldContain($"href=\"{SupervisionPage(id)}\"");
        crestmont.ShouldNotContain($"href=\"{SupervisionPage(id)}\"");
    }

    [Fact]
    public async Task Bank_users_cannot_open_the_supervision_pages()
    {
        var (_, id) = await SubmittedAsync(3, year: 2028);
        using var checker = await _bank.CheckerAsync();

        var response = await checker.GetAsync(new Uri(SupervisionPage(id), UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    public void Dispose() => _factory.Dispose();

    private static string BankPage(Guid id) => $"/bank/returns/{id}";

    private static string SupervisionPage(Guid id) => $"/supervision/returns/{id}";

    /// <summary>A new Harbourline return with valid figures, saved (and so validated) by the maker.</summary>
    private async Task<(Guid ObligationId, Guid SubmissionId)> DraftAsync(int month, int year = 2027)
    {
        var obligationId = await _bank.NewMlrObligationAsync(month, year);
        using var maker = await _bank.MakerAsync();
        var id = await BankPortal.StartDraftAsync(maker, obligationId);
        (await BankPortal.SaveAsync(maker, id, BankPortal.ValidMlr)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return (obligationId, id);
    }

    private async Task<(Guid ObligationId, Guid SubmissionId)> SubmittedAsync(int month, int year = 2027)
    {
        var draft = await DraftAsync(month, year);
        using var checker = await _bank.CheckerAsync();
        var page = BankPage(draft.SubmissionId);
        var response = await SupervisionPortal.PostStepAsync(checker, page, $"{page}/submit", "Checked.");
        PortalForms.RedirectPath(response).ShouldBe(page);
        return draft;
    }

    private async Task<(Guid ObligationId, Guid SubmissionId)> UnderReviewAsync(int month)
    {
        var submitted = await SubmittedAsync(month);
        using var reviewer = await _supervision.ReviewerAsync();
        var page = SupervisionPage(submitted.SubmissionId);
        PortalForms.RedirectPath(await SupervisionPortal.PostStepAsync(reviewer, page, $"{page}/start-review")).ShouldBe(page);
        return submitted;
    }
}
