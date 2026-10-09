using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using RegReturns.Application.Identity;
using RegReturns.Infrastructure.Identity.Wso2;

namespace RegReturns.UnitTests.Identity;

public sealed class Wso2IdentityDirectoryTests : IDisposable
{
    private const string TokenEndpoint = "https://iam.valoria.test/oauth2/token";
    private const string Users = "https://iam.valoria.test/scim2/Users";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero));
    private readonly Wso2ProvisionerOptions _provisioner = new() { ClientId = "prov:client", ClientSecret = "s3cret&more" };
    private readonly List<Recorded> _requests = [];
    private readonly Wso2IdentityDirectory _directory;
    private Func<HttpRequestMessage, HttpResponseMessage> _scim = _ => Json("""{"totalResults":0}""");
    private HttpStatusCode _tokenStatus = HttpStatusCode.OK;
    private int _tokens;

    public Wso2IdentityDirectoryTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Wso2Backchannel.HttpClientName).Returns(_ => new HttpClient(new StubHandler(this), disposeHandler: true));
        _directory = new Wso2IdentityDirectory(
            factory,
            Options.Create(new Wso2Options { Authority = new Uri("https://iam.valoria.test/") }),
            Options.Create(_provisioner),
            _clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_provisioner_asks_for_a_token_with_only_the_user_scopes_it_needs()
    {
        await _directory.FindByUserNameAsync("new.approver", Ct);

        var token = _requests[0];
        token.Uri.ShouldBe(TokenEndpoint);
        token.Method.ShouldBe(HttpMethod.Post);
        token.Body.ShouldContain("grant_type=client_credentials");
        token.Body.ShouldContain("scope=internal_user_mgt_list+internal_user_mgt_view+internal_user_mgt_update");
        token.Body.ShouldNotContain("s3cret");

        // RFC 6749 §2.3.1: id and secret are form-encoded before Base64.
        Encoding.UTF8.GetString(Convert.FromBase64String(token.Authorization!.Replace("Basic ", string.Empty, StringComparison.Ordinal)))
            .ShouldBe("prov%3Aclient:s3cret%26more");
    }

    [Fact]
    public async Task An_account_is_found_by_exact_user_name_with_its_application_roles()
    {
        _scim = _ => Json("""
            {"totalResults":1,"Resources":[{"id":"wso2-1","userName":"new.approver","roles":[
              {"display":"supervisor_approver","audienceType":"application"},
              {"display":"everyone","audienceType":"organization"}]}]}
            """);

        var account = await _directory.FindByUserNameAsync("new.approver", Ct);

        account.ShouldBe(new IdentityAccount("wso2-1", "new.approver", account!.PortalRoles));
        account.PortalRoles.ShouldBe(["supervisor_approver"]);
        var search = _requests[1];
        search.Method.ShouldBe(HttpMethod.Get);
        Uri.UnescapeDataString(search.Uri).ShouldBe($"{Users}?filter=userName eq \"new.approver\"&attributes=userName,roles");
        search.Authorization.ShouldBe("Bearer token-1");
    }

    [Fact]
    public async Task No_matching_account_is_null()
    {
        _scim = _ => Json("""{"totalResults":1,"Resources":[{"id":"wso2-9","userName":"new.approver.two"}]}""");

        (await _directory.FindByUserNameAsync("new.approver", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Opening_a_window_sets_the_enrolment_attribute_in_unix_milliseconds()
    {
        var until = new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);
        _scim = _ => Json("{}");

        await _directory.OpenTotpEnrolmentAsync("wso2-1", until, Ct);

        var patch = _requests[1];
        patch.Method.ShouldBe(HttpMethod.Patch);
        patch.Uri.ShouldBe($"{Users}/wso2-1");
        patch.ContentType.ShouldBe("application/scim+json");
        var body = JsonNode.Parse(patch.Body)!;
        body["schemas"]![0]!.GetValue<string>().ShouldBe("urn:ietf:params:scim:api:messages:2.0:PatchOp");
        var operation = body["Operations"]![0]!;
        operation["op"]!.GetValue<string>().ShouldBe("replace");
        operation["value"]![TotpEnrolmentClaim.ScimSchema]![TotpEnrolmentClaim.ScimAttribute]!.GetValue<string>()
            .ShouldBe(until.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task The_token_is_reused_until_a_minute_before_it_expires()
    {
        await _directory.FindByUserNameAsync("a.user", Ct);
        _clock.Advance(TimeSpan.FromMinutes(58));
        await _directory.FindByUserNameAsync("a.user", Ct);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _directory.FindByUserNameAsync("a.user", Ct);

        _requests.Count(r => r.Uri == TokenEndpoint).ShouldBe(2);
        _requests[^1].Authorization.ShouldBe("Bearer token-2");
    }

    [Fact]
    public async Task A_refusal_from_wso2_is_a_directory_failure_naming_the_status_only()
    {
        _scim = _ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("secret detail") };

        var failure = await Should.ThrowAsync<IdentityDirectoryException>(() => _directory.FindByUserNameAsync("a.user", Ct));

        failure.Message.ShouldBe("WSO2 answered GET /scim2/Users with 403.");
    }

    [Fact]
    public async Task Rejected_provisioner_credentials_are_a_directory_failure()
    {
        _tokenStatus = HttpStatusCode.Unauthorized;

        var failure = await Should.ThrowAsync<IdentityDirectoryException>(() => _directory.FindByUserNameAsync("a.user", Ct));

        failure.Message.ShouldBe("WSO2 answered POST /oauth2/token with 401.");
        _requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_unreachable_wso2_is_a_directory_failure()
    {
        _scim = _ => throw new HttpRequestException("Connection refused");

        await Should.ThrowAsync<IdentityDirectoryException>(() => _directory.FindByUserNameAsync("a.user", Ct));
    }

    [Fact]
    public async Task Without_credentials_nothing_is_sent()
    {
        _provisioner.ClientSecret = null;

        var failure = await Should.ThrowAsync<IdentityDirectoryException>(() => _directory.FindByUserNameAsync("a.user", Ct));

        failure.Message.ShouldContain("Iam:Provisioner");
        _requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_quote_cannot_break_out_of_the_filter()
    {
        await _directory.FindByUserNameAsync("a\" or userName pr \"", Ct);

        Uri.UnescapeDataString(_requests[1].Uri).ShouldContain("filter=userName eq \"a or userName pr \"&");
    }

    public void Dispose() => _directory.Dispose();

    private HttpResponseMessage Respond(HttpRequestMessage request)
    {
        if (request.RequestUri!.AbsoluteUri == TokenEndpoint)
        {
            return _tokenStatus == HttpStatusCode.OK
                ? Json($$"""{"access_token":"token-{{++_tokens}}","token_type":"Bearer","expires_in":3600}""")
                : new HttpResponseMessage(_tokenStatus);
        }

        return _scim(request);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/scim+json") };

    private sealed record Recorded(HttpMethod Method, string Uri, string? Authorization, string? ContentType, string Body);

    private sealed class StubHandler(Wso2IdentityDirectoryTests test) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            test._requests.Add(new Recorded(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.MediaType,
                body));
            return test.Respond(request);
        }
    }
}
