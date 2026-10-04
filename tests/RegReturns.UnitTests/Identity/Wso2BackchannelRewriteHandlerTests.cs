using System.Net;

using RegReturns.Infrastructure.Identity.Wso2;

namespace RegReturns.UnitTests.Identity;

public sealed class Wso2BackchannelRewriteHandlerTests
{
    private static readonly Uri PublicAuthority = new("https://iam.valoria.test/");
    private static readonly Uri Backchannel = new("https://wso2:9443/");

    [Fact]
    public async Task Public_url_is_sent_to_the_backchannel_origin()
    {
        var sent = await RewriteAsync("https://iam.valoria.test/oauth2/jwks");

        sent.ShouldBe("https://wso2:9443/oauth2/jwks");
    }

    [Fact]
    public async Task Path_and_query_are_preserved()
    {
        var sent = await RewriteAsync("https://iam.valoria.test/oauth2/token/.well-known/openid-configuration?tenant=carbon.super&x=%2F");

        sent.ShouldBe("https://wso2:9443/oauth2/token/.well-known/openid-configuration?tenant=carbon.super&x=%2F");
    }

    [Fact]
    public async Task Host_is_compared_case_insensitively()
    {
        var sent = await RewriteAsync("https://IAM.Valoria.Test/oauth2/jwks");

        sent.ShouldBe("https://wso2:9443/oauth2/jwks");
    }

    [Fact]
    public async Task Explicit_default_port_counts_as_the_same_origin()
    {
        var sent = await RewriteAsync("https://iam.valoria.test:443/oauth2/jwks");

        sent.ShouldBe("https://wso2:9443/oauth2/jwks");
    }

    [Theory]
    [InlineData("https://elsewhere.example/oauth2/jwks")]
    [InlineData("https://iam.valoria.test:8443/oauth2/jwks")]
    [InlineData("http://iam.valoria.test/oauth2/jwks")]
    [InlineData("https://sub.iam.valoria.test/oauth2/jwks")]
    public async Task Other_origins_are_left_alone(string url)
    {
        var sent = await RewriteAsync(url);

        sent.ShouldBe(new Uri(url).AbsoluteUri);
    }

    [Fact]
    public async Task Requests_already_on_the_backchannel_are_left_alone()
    {
        var sent = await RewriteAsync("https://wso2:9443/oauth2/jwks");

        sent.ShouldBe("https://wso2:9443/oauth2/jwks");
    }

    [Fact]
    public async Task Same_public_and_backchannel_authority_changes_nothing()
    {
        var sent = await RewriteAsync("https://iam.valoria.test/oauth2/jwks", backchannel: PublicAuthority);

        sent.ShouldBe("https://iam.valoria.test/oauth2/jwks");
    }

    [Fact]
    public async Task Plain_http_backchannel_uses_its_own_scheme_and_port()
    {
        var sent = await RewriteAsync("https://iam.valoria.test/oauth2/jwks", backchannel: new Uri("http://wso2:9763/"));

        sent.ShouldBe("http://wso2:9763/oauth2/jwks");
    }

    private static async Task<string?> RewriteAsync(string url, Uri? backchannel = null)
    {
        var inner = new CapturingHandler();
        using var invoker = new HttpMessageInvoker(new Wso2BackchannelRewriteHandler(PublicAuthority, backchannel ?? Backchannel) { InnerHandler = inner });
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));

        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return inner.RequestUri?.AbsoluteUri;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
