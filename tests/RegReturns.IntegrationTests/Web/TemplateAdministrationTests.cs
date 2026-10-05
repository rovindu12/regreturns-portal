using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Application.Identity;
using RegReturns.Domain.Templates;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;
using RegReturns.Web.Controllers;
using RegReturns.Web.Models.Templates;
using RegReturns.Web.Navigation;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// The template administration screens against a database of this class's own, since the tests start, change, publish
/// and delete template versions. Each return type has one job so the tests do not depend on their order: MLR gets a
/// new published version, MDA drafts are started and deleted, and QCAR version 1 is only ever read or refused.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class TemplateAdministrationTests : IClassFixture<PortalDatabaseFixture>, IDisposable
{
    private const string Mlr = "MLR";
    private const string Mda = "MDA";
    private const string Qcar = "QCAR";
    private const string NewFieldCode = "STRESSED_OUTFLOWS";
    private const string NewRuleCode = "MLR_STRESS_CAP";

    private static readonly string Catalogue = "/" + TemplatesController.RoutePrefix;

    private readonly PortalDatabaseFixture _database;
    private readonly WebApplicationFactory<Program> _factory;

    public TemplateAdministrationTests(PortalDatabaseFixture database)
    {
        _database = database;

        // The seed date (4 October 2026) makes the default effective date of a new draft 1 November 2026.
        _factory = PortalHost.Create(
            database.ConnectionString,
            services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(SqlServerFixture.SeedDate)));
    }

    [Fact]
    public async Task Catalogue_lists_the_seeded_return_types_with_their_published_versions()
    {
        using var client = Admin();
        var (_, mlrV1) = await VersionOneAsync(Mlr);
        var (_, mdaV1) = await VersionOneAsync(Mda);
        var (_, qcarV1) = await VersionOneAsync(Qcar);

        var html = await PortalForms.GetTextAsync(client, Catalogue);

        html.ShouldContain("Monthly Liquidity Return (MLR)");
        html.ShouldContain("Monthly Deposits and Advances Return (MDA)");
        html.ShouldContain("Quarterly Capital Adequacy Return (QCAR)");
        html.ShouldContain($"href=\"{VersionPath(mlrV1)}\"");
        html.ShouldContain($"href=\"{VersionPath(mdaV1)}\"");
        html.ShouldContain($"href=\"{VersionPath(qcarV1)}\"");
        html.ShouldContain(TemplateDisplay.StatusLabel(TemplateStatus.Published));
        html.ShouldContain("value=\"2026-11-01\"");
    }

    [Fact]
    public async Task Admin_landing_page_links_to_the_template_catalogue()
    {
        using var client = Admin();

        var html = await PortalForms.GetHtmlAsync(client, PortalAreas.Admin.Path);

        html.ShouldContain($"href=\"{Catalogue}\"");
    }

    [Fact]
    public async Task Bank_maker_is_forbidden_from_template_administration()
    {
        using var client = SignedIn(
            (ClaimNames.Subject, "maker-templates"), (ClaimNames.Name, "Maker (Harbourline Bank PLC)"),
            (ClaimNames.Roles, RoleNames.BankMaker), (ClaimNames.InstitutionId, "HBL"));

        var response = await client.GetAsync(new Uri(Catalogue, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Administrator_starts_a_draft_adds_a_field_and_a_rule_and_publishes_it()
    {
        using var client = Admin();
        var (mlrId, _) = await VersionOneAsync(Mlr);

        // 1. Start a draft, copied from the published version.
        var started = await PortalForms.PostAsync(client, Catalogue, Catalogue + "/drafts",
            ("returnTypeId", mlrId.ToString()), ("effectiveFrom", "2026-11-01"));
        var draft = PortalForms.RedirectPath(started);
        var draftPage = await PortalForms.FollowAsync(client, started);
        draftPage.ShouldContain("Draft started.");
        draftPage.ShouldContain("Monthly Liquidity Return, version 2");

        // 2. Add a field.
        var fieldAdded = await PortalForms.PostAsync(client, draft, draft + "/fields",
            (nameof(FieldForm.Code), NewFieldCode), (nameof(FieldForm.Label), "Stressed outflows"),
            (nameof(FieldForm.Section), "30-day stressed cash flows"), (nameof(FieldForm.DataType), nameof(FieldDataType.Amount)),
            (nameof(FieldForm.Unit), "VLD m"), (nameof(FieldForm.Precision), "2"));
        (await PortalForms.FollowAsync(client, fieldAdded)).ShouldContain($"Field {NewFieldCode} added");

        // 3. A cross-field rule whose expression does not parse is refused, and the form comes back as typed.
        var refused = await PortalForms.PostAsync(client, draft, draft + "/rules", CrossFieldRule("[CASH_OUTFLOWS_30D] *"));
        var refusedPage = await PortalForms.FollowAsync(client, refused);
        refusedPage.ShouldContain("Not saved.");
        refusedPage.ShouldContain("Right side: The expression ends too early");
        refusedPage.ShouldContain("value=\"[CASH_OUTFLOWS_30D] *\"");

        // 4. The corrected rule is added.
        var ruleAdded = await PortalForms.PostAsync(client, draft, draft + "/rules", CrossFieldRule("1.5 * [CASH_OUTFLOWS_30D]"));
        (await PortalForms.FollowAsync(client, ruleAdded)).ShouldContain($"Rule {NewRuleCode} added");

        // 5. Publish.
        var published = await PortalForms.PostAsync(client, draft, draft + "/publish", ("confirm", "true"));
        var publishedPage = await PortalForms.FollowAsync(client, published);
        publishedPage.ShouldContain("Version published.");
        publishedPage.ShouldContain("This version is published");
        publishedPage.ShouldNotContain("Add a field");

        await using var context = SqlServerFixture.CreateContext(_database.ConnectionString);
        var version = await context.TemplateVersions.AsNoTracking().Include(v => v.Fields).Include(v => v.Rules)
            .SingleAsync(v => v.ReturnTypeId == mlrId && v.Version == 2, TestContext.Current.CancellationToken);
        version.Status.ShouldBe(TemplateStatus.Published);
        version.EffectiveFrom.ShouldBe(new DateOnly(2026, 11, 1));
        version.Fields.ShouldContain(f => f.Code == NewFieldCode);
        version.Rules.ShouldContain(r => r.Code == NewRuleCode && r.RightExpression == "1.5 * [CASH_OUTFLOWS_30D]");
    }

    [Fact]
    public async Task Changing_a_published_version_is_refused_with_a_message()
    {
        using var client = Admin();
        var (_, qcarV1) = await VersionOneAsync(Qcar);
        var page = VersionPath(qcarV1);

        var response = await PortalForms.PostAsync(client, page, page + "/fields", NewField("EXTRA_BUFFER"));

        var html = await PortalForms.FollowAsync(client, response);
        html.ShouldContain(TemplateErrors.NotDraft.Message);
        (await FieldCodesAsync(qcarV1)).ShouldNotContain("EXTRA_BUFFER");
    }

    [Fact]
    public async Task Template_changes_without_an_antiforgery_token_are_rejected()
    {
        using var client = Admin();
        var (_, qcarV1) = await VersionOneAsync(Qcar);
        using var form = new FormUrlEncodedContent(
            NewField("NO_TOKEN").Select(f => new KeyValuePair<string, string>(f.Name, f.Value)));

        var response = await client.PostAsync(
            new Uri(VersionPath(qcarV1) + "/fields", UriKind.Relative), form, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rule_limit_written_with_a_comma_is_refused_with_a_friendly_message()
    {
        using var client = Admin();
        var draft = await StartDraftAsync(client, Mda);
        try
        {
            var response = await PortalForms.PostAsync(client, draft, draft + "/rules",
                (nameof(RuleForm.Code), "MDA_DEPOSITS_FLOOR"), (nameof(RuleForm.RuleType), nameof(RuleType.Range)),
                (nameof(RuleForm.Severity), nameof(Severity.Warning)), (nameof(RuleForm.TargetFieldCode), "TOTAL_DEPOSITS"),
                (nameof(RuleForm.Message), "Deposits look too low."), (nameof(RuleForm.MinValue), "1,5"));

            var html = await PortalForms.FollowAsync(client, response);
            html.ShouldContain("The minimum must be a number written with a dot for decimals");
            html.ShouldContain("value=\"1,5\"");
        }
        finally
        {
            await DeleteDraftAsync(client, draft);
        }
    }

    [Fact]
    public async Task Field_used_by_rules_cannot_be_removed_and_the_page_says_why()
    {
        using var client = Admin();
        var draft = await StartDraftAsync(client, Mda);
        try
        {
            var html = await PortalForms.GetHtmlAsync(client, draft);

            // TOTAL_DEPOSITS is checked by the seeded MDA rules, so its remove button is disabled and explains why.
            html.ShouldMatch("<button type=\"button\" class=\"btn btn-outline-danger btn-sm\" disabled title=\"Rules [^\"]+ use this field\\.[^\"]*\" aria-describedby=\"remove-reason-TOTAL_DEPOSITS\">");
        }
        finally
        {
            await DeleteDraftAsync(client, draft);
        }
    }

    [Fact]
    public async Task Deleting_a_draft_returns_to_the_catalogue_and_removes_it()
    {
        using var client = Admin();
        var draft = await StartDraftAsync(client, Mda);
        var draftId = Guid.Parse(draft[(draft.LastIndexOf('/') + 1)..]);

        var response = await PortalForms.PostAsync(client, draft, draft + "/delete", ("confirm", "true"));

        PortalForms.RedirectPath(response).ShouldBe(Catalogue);
        (await PortalForms.FollowAsync(client, response)).ShouldContain("Draft deleted.");
        await using var context = SqlServerFixture.CreateContext(_database.ConnectionString);
        (await context.TemplateVersions.AnyAsync(v => v.Id == draftId, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    public void Dispose() => _factory.Dispose();

    private static string VersionPath(Guid id) => $"{Catalogue}/{id}";

    private static (string Name, string Value)[] NewField(string code) =>
    [
        (nameof(FieldForm.Code), code), (nameof(FieldForm.Label), "Extra buffer"), (nameof(FieldForm.Section), "Buffers"),
        (nameof(FieldForm.DataType), nameof(FieldDataType.Amount)), (nameof(FieldForm.Unit), "VLD m"), (nameof(FieldForm.Precision), "2"),
    ];

    // Without JavaScript the browser posts every parameter group; the blank ones of other rule types are ignored.
    private static (string Name, string Value)[] CrossFieldRule(string right) =>
    [
        (nameof(RuleForm.Code), NewRuleCode), (nameof(RuleForm.RuleType), nameof(RuleType.CrossField)),
        (nameof(RuleForm.Severity), nameof(Severity.Warning)), (nameof(RuleForm.TargetFieldCode), NewFieldCode),
        (nameof(RuleForm.Message), "Stressed outflows are above 150% of 30-day outflows."),
        (nameof(RuleForm.MinValue), string.Empty), (nameof(RuleForm.MaxValue), string.Empty),
        (nameof(RuleForm.LeftExpression), $"[{NewFieldCode}]"), (nameof(RuleForm.Operator), nameof(ComparisonOperator.LessThanOrEqual)),
        (nameof(RuleForm.RightExpression), right), (nameof(RuleForm.Tolerance), "0"),
        (nameof(RuleForm.ThresholdPercent), string.Empty), (nameof(RuleForm.VarianceBasis), nameof(VarianceBasis.PreviousPeriod)),
    ];

    private async Task<string> StartDraftAsync(HttpClient client, string returnTypeCode)
    {
        var (returnTypeId, _) = await VersionOneAsync(returnTypeCode);
        var response = await PortalForms.PostAsync(client, Catalogue, Catalogue + "/drafts",
            ("returnTypeId", returnTypeId.ToString()), ("effectiveFrom", "2027-01-01"));
        var draft = PortalForms.RedirectPath(response);
        draft.ShouldNotBe(Catalogue, "the draft was not started");
        return draft;
    }

    private static async Task DeleteDraftAsync(HttpClient client, string draft) =>
        PortalForms.RedirectPath(await PortalForms.PostAsync(client, draft, draft + "/delete", ("confirm", "true"))).ShouldBe(Catalogue);

    private async Task<(Guid ReturnTypeId, Guid VersionId)> VersionOneAsync(string returnTypeCode)
    {
        await using var context = SqlServerFixture.CreateContext(_database.ConnectionString);
        var returnTypeId = await context.ReturnTypes.Where(r => r.Code == returnTypeCode).Select(r => r.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
        var versionId = await context.TemplateVersions.Where(v => v.ReturnTypeId == returnTypeId && v.Version == 1).Select(v => v.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
        return (returnTypeId, versionId);
    }

    private async Task<List<string>> FieldCodesAsync(Guid versionId)
    {
        await using var context = SqlServerFixture.CreateContext(_database.ConnectionString);
        return await context.TemplateVersions.Where(v => v.Id == versionId).SelectMany(v => v.Fields.Select(f => f.Code))
            .ToListAsync(TestContext.Current.CancellationToken);
    }

    // Two-step verification (amr=totp) is what the Admin.Manage policy needs on top of the role.
    private HttpClient Admin() => SignedIn(
        (ClaimNames.Subject, "template-admin"), (ClaimNames.Name, "Portal Admin"), (ClaimNames.Roles, RoleNames.SystemAdmin),
        (ClaimNames.AuthenticationMethods, "BasicAuthenticator"), (ClaimNames.AuthenticationMethods, "totp"));

    // Pages render antiforgery-protected forms whose cookies are Secure-only: use HTTPS.
    private HttpClient SignedIn(params (string Type, string Value)[] claims)
    {
        var client = _factory.CreateClientAs(claims);
        client.BaseAddress = PortalHost.BaseAddress;
        return client;
    }
}
