using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using NSubstitute;

using RegReturns.Api.Authentication;
using RegReturns.Application.Identity;
using RegReturns.Infrastructure.Identity.Wso2;

namespace RegReturns.UnitTests.Api;

public sealed class ConfigureApiJwtBearerOptionsTests : IDisposable
{
    private readonly HttpClient _backchannel = new();
    private readonly ConfigureApiJwtBearerOptions _configure;

    public ConfigureApiJwtBearerOptionsTests()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Wso2Backchannel.HttpClientName).Returns(_backchannel);
        _configure = new ConfigureApiJwtBearerOptions(
            Options.Create(new Wso2Options { Authority = new Uri("https://iam.example.test/") }), factory);
    }

    [Fact]
    public void Signing_keys_come_from_wso2_metadata_through_the_back_channel_client()
    {
        var options = Configured();

        options.MetadataAddress.ShouldBe("https://iam.example.test/oauth2/token/.well-known/openid-configuration");
        options.RequireHttpsMetadata.ShouldBeTrue();
        options.Backchannel.ShouldBeSameAs(_backchannel);
    }

    [Fact]
    public void The_framework_builds_a_refreshing_configuration_manager_on_that_client()
    {
        var options = Configured();

        new JwtBearerPostConfigureOptions().PostConfigure(JwtBearerDefaults.AuthenticationScheme, options);

        options.ConfigurationManager.ShouldBeOfType<ConfigurationManager<OpenIdConnectConfiguration>>();
        options.Backchannel.ShouldBeSameAs(_backchannel);
    }

    [Fact]
    public void Only_tokens_from_wso2_for_the_api_audience_are_valid()
    {
        var parameters = Configured().TokenValidationParameters;

        parameters.ValidIssuer.ShouldBe("https://iam.example.test/oauth2/token");
        parameters.ValidAudience.ShouldBe(ApiScopes.ApiIdentifier);
        parameters.ValidateIssuer.ShouldBeTrue();
        parameters.ValidateAudience.ShouldBeTrue();
        parameters.RequireAudience.ShouldBeTrue();
    }

    [Fact]
    public void Only_rs256_signed_access_tokens_are_valid()
    {
        var parameters = Configured().TokenValidationParameters;

        parameters.ValidAlgorithms.ShouldBe([SecurityAlgorithms.RsaSha256]);
        parameters.ValidTypes.ShouldBe(["at+jwt", "application/at+jwt"]);
        parameters.RequireSignedTokens.ShouldBeTrue();
    }

    [Fact]
    public void Lifetimes_are_required_and_checked_with_thirty_seconds_of_skew()
    {
        var parameters = Configured().TokenValidationParameters;

        parameters.ClockSkew.ShouldBe(TimeSpan.FromSeconds(30));
        parameters.RequireExpirationTime.ShouldBeTrue();
        parameters.ValidateLifetime.ShouldBeTrue();
    }

    [Fact]
    public void Claims_keep_their_jwt_names_and_the_subject_is_the_name()
    {
        var options = Configured();

        options.MapInboundClaims.ShouldBeFalse();
        options.TokenValidationParameters.NameClaimType.ShouldBe(ClaimNames.Subject);
    }

    [Fact]
    public void Tokens_are_not_kept_after_validation()
    {
        Configured().SaveToken.ShouldBeFalse();
    }

    [Fact]
    public void Events_come_from_the_api_events_type()
    {
        Configured().EventsType.ShouldBe(typeof(ApiJwtBearerEvents));
    }

    [Fact]
    public void Other_schemes_are_left_alone()
    {
        var options = new JwtBearerOptions();

        _configure.Configure("Other", options);

        options.MetadataAddress.ShouldBeNull();
        options.Backchannel.ShouldBeNull();
    }

    public void Dispose() => _backchannel.Dispose();

    private JwtBearerOptions Configured()
    {
        var options = new JwtBearerOptions();
        _configure.Configure(JwtBearerDefaults.AuthenticationScheme, options);
        return options;
    }
}
