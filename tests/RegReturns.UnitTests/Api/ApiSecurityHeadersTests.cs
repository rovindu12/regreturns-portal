using RegReturns.Api.Hosting;

namespace RegReturns.UnitTests.Api;

public sealed class ApiSecurityHeadersTests
{
    [Fact]
    public void Api_answers_allow_no_content_and_no_framing()
    {
        ApiSecurityHeaders.ApiPolicy.ShouldStartWith("default-src 'none';");
        ApiSecurityHeaders.ApiPolicy.ShouldContain("frame-ancestors 'none'");
        ApiSecurityHeaders.ApiPolicy.ShouldContain("form-action 'none'");
    }

    [Fact]
    public void Swagger_runs_only_its_own_scripts_and_asks_only_the_identity_provider_for_tokens()
    {
        var policy = ApiSecurityHeaders.SwaggerPolicy("https://iam.example");

        policy.ShouldContain("script-src 'self';");
        policy.ShouldContain("connect-src 'self' https://iam.example;");
        policy.ShouldContain("frame-ancestors 'none'");
        policy.ShouldNotContain("unsafe-eval");
        policy.ShouldNotContain("script-src 'self' 'unsafe-inline'");
    }
}
