extern alias IamBootstrapTool;

using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using IamBootstrapTool::RegReturns.IamBootstrap;
using IamBootstrapTool::RegReturns.IamBootstrap.Steps;
using IamBootstrapTool::RegReturns.IamBootstrap.Wso2;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Infrastructure.Identity.Wso2;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class DemoTotpStepTests : IDisposable
{
    private const string UserName = "approver.mfa";
    private const string Key = "TOTP_SECRET_APPROVER_MFA";
    private const string Known = "KNOWNSECRETKNOWN";
    private const string Issued = "JBSWY3DPEHPK3PXP";
    private const string DemoPassword = "demo-password-123";
    private const string Apps = "api/server/v1/applications";
    private const string Helper = $"{Apps}/helper-1";
    private const string TotpPath = "api/users/v1/me/totp";

    private readonly StubWso2 _wso2 = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 5, 9, 30, 12, TimeSpan.Zero));

    [Theory]
    [InlineData("approver.mfa", "TOTP_SECRET_APPROVER_MFA")]
    [InlineData("Admin-MFA@valoria", "TOTP_SECRET_ADMIN_MFA_VALORIA")]
    [InlineData("checker2", "TOTP_SECRET_CHECKER2")]
    [InlineData("josé.mfa", "TOTP_SECRET_JOS__MFA")]
    public void Secret_key_is_the_upper_case_user_name_with_underscores(string userName, string expected)
    {
        DemoTotpStep.SecretKey(userName).ShouldBe(expected);
    }

    [Fact]
    public async Task Known_secret_of_an_enrolled_user_is_kept_without_signing_in()
    {
        UserExists(totpEnabled: true);

        var state = await RunAsync(known: Known);

        _wso2.Writes().ShouldBeEmpty();
        state.GeneratedSettings[Key].ShouldBe(Known);
        OutcomeOf(state).ShouldBe(Outcome.Unchanged);
    }

    [Fact]
    public async Task Missing_secret_is_enrolled_and_written_to_the_generated_settings()
    {
        UserExists(totpEnabled: false);
        HelperCanBeCreated();
        UserCanSignIn();
        EnrolmentSucceeds();

        var state = await RunAsync(known: null);

        state.GeneratedSettings[Key].ShouldBe(Issued);
        OutcomeOf(state).ShouldBe(Outcome.Created);
    }

    [Fact]
    public async Task Enrolment_validates_the_code_computed_from_the_issued_secret()
    {
        UserExists(totpEnabled: false);
        HelperCanBeCreated();
        UserCanSignIn();
        EnrolmentSucceeds();

        await RunAsync(known: null);

        var actions = TotpCalls().Select(r => r.Body!["action"]!.GetValue<string>());
        actions.ShouldBe(["INIT", "VALIDATE"]);
        TotpCalls()[1].Body!["verificationCode"]!.GetValue<string>().ShouldBe(Totp.Code(Issued, _time.GetUtcNow()));
    }

    [Fact]
    public async Task Enrolment_calls_are_made_with_the_users_own_token()
    {
        UserExists(totpEnabled: false);
        HelperCanBeCreated();
        UserCanSignIn();
        EnrolmentSucceeds();

        await RunAsync(known: null);

        TotpCalls().ShouldAllBe(r => r.Authorization!.Scheme == "Bearer" && r.Authorization.Parameter == "user-token");
    }

    [Fact]
    public async Task User_signs_in_with_the_password_grant_through_the_helper_app()
    {
        UserExists(totpEnabled: false);
        HelperCanBeCreated();
        UserCanSignIn();
        EnrolmentSucceeds();

        await RunAsync(known: null);

        var token = _wso2.Requests.Single(r => r.PathAndQuery == "oauth2/token");
        token.Text!.Split('&').ShouldBe(
            ["grant_type=password", "username=approver.mfa", $"password={DemoPassword}", "scope=internal_login"], ignoreOrder: true);
        token.Authorization!.ShouldBe(Wso2AdminClient.BasicCredentials("regreturns-totp-enrolment", "helper-secret"));
    }

    [Fact]
    public async Task Helper_app_allows_only_the_password_grant()
    {
        UserExists(totpEnabled: false);
        HelperCanBeCreated();
        UserCanSignIn();
        EnrolmentSucceeds();

        await RunAsync(known: null);

        var create = _wso2.Writes().First(r => r.Method == HttpMethod.Post && r.PathAndQuery == Apps).Body!;
        var oidc = create["inboundProtocolConfiguration"]!["oidc"]!;
        oidc["grantTypes"]!.AsArray().Select(g => g!.GetValue<string>()).ShouldBe(["password"]);
        oidc["clientId"]!.GetValue<string>().ShouldBe("regreturns-totp-enrolment");
    }

    [Fact]
    public async Task Helper_app_is_deleted_after_enrolment()
    {
        UserExists(totpEnabled: false);
        HelperCanBeCreated();
        UserCanSignIn();
        EnrolmentSucceeds();

        await RunAsync(known: null);

        _wso2.Requests[^1].ShouldSatisfyAllConditions(
            r => r.Method.ShouldBe(HttpMethod.Delete),
            r => r.PathAndQuery.ShouldBe(Helper));
    }

    [Fact]
    public async Task Helper_app_is_deleted_when_wso2_rejects_the_code()
    {
        UserExists(totpEnabled: false);
        HelperCanBeCreated();
        UserCanSignIn();
        _wso2.OnJson(HttpMethod.Post, TotpPath, Init(), once: true)
            .OnJson(HttpMethod.Post, TotpPath, """{"isValid":false}""");

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => RunAsync(known: null));

        failure.Message.ShouldContain("did not accept the computed TOTP code");
        HelperDeleted().ShouldBeTrue();
    }

    [Fact]
    public async Task Helper_app_is_deleted_when_the_user_cannot_sign_in()
    {
        UserExists(totpEnabled: false);
        HelperCanBeCreated();
        _wso2.On(HttpMethod.Post, "oauth2/token", HttpStatusCode.BadRequest, new JsonObject { ["error"] = "invalid_grant" });

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => RunAsync(known: null));

        failure.Message.ShouldContain("DEMO_USER_PASSWORD");
        failure.Message.ShouldNotContain(DemoPassword);
        HelperDeleted().ShouldBeTrue();
    }

    [Fact]
    public async Task Known_secret_is_replaced_when_the_user_has_no_active_totp()
    {
        UserExists(totpEnabled: false);
        HelperCanBeCreated();
        UserCanSignIn();
        _wso2.On(HttpMethod.Get, $"{TotpPath}/secret", HttpStatusCode.NotFound);
        EnrolmentSucceeds();

        var state = await RunAsync(known: Known);

        state.GeneratedSettings[Key].ShouldBe(Issued);
        OutcomeOf(state).ShouldBe(Outcome.Updated);
    }

    [Fact]
    public async Task Demo_reset_keeps_a_known_secret_that_wso2_still_holds()
    {
        UserExists(totpEnabled: true);
        HelperCanBeCreated();
        UserCanSignIn();
        _wso2.OnJson(HttpMethod.Get, $"{TotpPath}/secret", $$"""{"secret":"{{Known}}"}""");

        var state = await RunAsync(known: Known, reset: true);

        TotpCalls().ShouldBeEmpty();
        state.GeneratedSettings[Key].ShouldBe(Known);
        OutcomeOf(state).ShouldBe(Outcome.Unchanged);
        HelperDeleted().ShouldBeTrue();
    }

    [Fact]
    public async Task Missing_mfa_user_stops_the_step_before_any_app_is_created()
    {
        _wso2.OnJson(HttpMethod.Get, "scim2/Users", """{"Resources":[]}""");

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => RunAsync(known: null));

        failure.Message.ShouldContain("does not exist");
        _wso2.Writes().ShouldBeEmpty();
    }

    [Fact]
    public async Task User_listed_twice_is_handled_once()
    {
        UserExists(totpEnabled: true);

        var state = await RunAsync(known: Known, users: [UserName, "Approver.MFA"]);

        state.Changes.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task No_mfa_users_means_no_calls()
    {
        var state = await RunAsync(known: null, users: []);

        _wso2.Requests.ShouldBeEmpty();
        state.Changes.ShouldBeEmpty();
    }

    public void Dispose() => _wso2.Dispose();

    private static string Init()
    {
        var uri = $"otpauth://totp/carbon.super:{UserName}?secret={Issued}&issuer=carbon.super";
        return new JsonObject { ["qrCodeUrl"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(uri)) }.ToJsonString();
    }

    private void UserExists(bool totpEnabled) =>
        _wso2.OnJson(HttpMethod.Get, "scim2/Users", $$"""{"Resources":[{"id":"user-1","userName":"{{UserName}}"}]}""")
            .On(
                HttpMethod.Get,
                "scim2/Users/user-1",
                HttpStatusCode.OK,
                new JsonObject { ["id"] = "user-1", [Wso2Scim.Wso2UserSchema] = new JsonObject { ["totpEnabled"] = totpEnabled } });

    private void HelperCanBeCreated() =>
        _wso2.OnJson(HttpMethod.Get, Apps, """{"applications":[]}""")
            .On(HttpMethod.Post, Apps, HttpStatusCode.Created, location: $"{StubWso2.Authority}{Helper}")
            .OnJson(HttpMethod.Get, $"{Helper}/inbound-protocols/oidc", """{"clientId":"regreturns-totp-enrolment","clientSecret":"helper-secret"}""")
            .On(HttpMethod.Delete, Helper, HttpStatusCode.NoContent);

    private void UserCanSignIn() =>
        _wso2.OnJson(HttpMethod.Post, "oauth2/token", """{"access_token":"user-token","token_type":"Bearer"}""");

    private void EnrolmentSucceeds() =>
        _wso2.OnJson(HttpMethod.Post, TotpPath, Init(), once: true)
            .OnJson(HttpMethod.Post, TotpPath, """{"isValid":true}""");

    private List<RecordedRequest> TotpCalls() => [.. _wso2.Requests.Where(r => r.Method == HttpMethod.Post && r.PathAndQuery == TotpPath)];

    private bool HelperDeleted() => _wso2.Requests.Any(r => r.Method == HttpMethod.Delete && r.PathAndQuery == Helper);

    private static Outcome OutcomeOf(BootstrapState state)
    {
        var change = state.Changes.ShouldHaveSingleItem();
        change.Kind.ShouldBe("TOTP enrolment");
        change.Name.ShouldBe(UserName);
        return change.Outcome;
    }

    private async Task<BootstrapState> RunAsync(string? known, bool reset = false, string[]? users = null)
    {
        var state = new BootstrapState { ResetDemoUsers = reset };
        if (known is not null)
        {
            state.ExistingSettings[Key] = known;
        }

        var options = Options.Create(new BootstrapOptions { MfaAlwaysUsers = users ?? [UserName], DemoUserPassword = DemoPassword });
        var client = _wso2.Client();
        var step = new DemoTotpStep(
            new Wso2Applications(client),
            new Wso2Scim(client),
            _wso2.Factory(),
            Options.Create(new Wso2Options { Authority = StubWso2.Authority }),
            options,
            _time);
        await step.RunAsync(state, TestContext.Current.CancellationToken);
        return state;
    }
}
