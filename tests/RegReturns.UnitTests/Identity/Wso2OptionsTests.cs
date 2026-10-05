using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using RegReturns.Infrastructure;
using RegReturns.Infrastructure.Identity.Wso2;

namespace RegReturns.UnitTests.Identity;

public sealed class Wso2OptionsTests
{
    [Theory]
    [InlineData("https://localhost:9443")]
    [InlineData("https://localhost:9443/")]
    public void Issuer_is_the_token_endpoint_of_the_authority(string authority)
    {
        Options(authority).Issuer.AbsoluteUri.ShouldBe("https://localhost:9443/oauth2/token");
    }

    [Theory]
    [InlineData("https://localhost:9443")]
    [InlineData("https://localhost:9443/")]
    public void Metadata_address_is_the_discovery_document_under_the_issuer(string authority)
    {
        Options(authority).MetadataAddress.AbsoluteUri.ShouldBe("https://localhost:9443/oauth2/token/.well-known/openid-configuration");
    }

    [Theory]
    [InlineData("https://localhost:9443")]
    [InlineData("https://localhost:9443/")]
    public void Jwks_address_is_under_the_authority(string authority)
    {
        Options(authority).JwksAddress.AbsoluteUri.ShouldBe("https://localhost:9443/oauth2/jwks");
    }

    [Theory]
    [InlineData("https://iam.valoria.test/wso2")]
    [InlineData("https://iam.valoria.test/wso2/")]
    public void Derived_addresses_keep_the_base_path_of_the_authority(string authority)
    {
        var options = Options(authority);

        options.Issuer.AbsoluteUri.ShouldBe("https://iam.valoria.test/wso2/oauth2/token");
        options.MetadataAddress.AbsoluteUri.ShouldBe("https://iam.valoria.test/wso2/oauth2/token/.well-known/openid-configuration");
        options.JwksAddress.AbsoluteUri.ShouldBe("https://iam.valoria.test/wso2/oauth2/jwks");
        options.TokenEndpoint.AbsoluteUri.ShouldBe("https://iam.valoria.test/wso2/oauth2/token");
    }

    [Fact]
    public void Backchannel_defaults_to_the_authority()
    {
        Options("https://localhost:9443/").EffectiveBackchannelAuthority.ShouldBe(new Uri("https://localhost:9443/"));
    }

    [Fact]
    public void Configured_backchannel_is_used_for_server_to_server_calls()
    {
        var options = new Wso2Options { Authority = new Uri("https://iam.valoria.test/"), BackchannelAuthority = new Uri("https://wso2:9443/") };

        options.EffectiveBackchannelAuthority.ShouldBe(new Uri("https://wso2:9443/"));
    }

    [Fact]
    public void Derived_addresses_need_an_authority()
    {
        Should.Throw<InvalidOperationException>(() => new Wso2Options().Issuer).Message.ShouldContain("Wso2:Authority");
    }

    [Fact]
    public void Backchannel_registration_accepts_an_https_authority()
    {
        using var provider = BuildBackchannel(new() { ["Wso2:Authority"] = "https://iam.valoria.test/" });

        provider.GetRequiredService<IOptions<Wso2Options>>().Value.Authority.ShouldBe(new Uri("https://iam.valoria.test/"));
    }

    [Fact]
    public void Backchannel_registration_refuses_a_plain_http_authority()
    {
        using var provider = BuildBackchannel(new() { ["Wso2:Authority"] = "http://iam.valoria.test/" });

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<Wso2Options>>().Value)
            .Message.ShouldContain("absolute https URL");
    }

    [Fact]
    public void Backchannel_registration_refuses_a_relative_authority()
    {
        using var provider = BuildBackchannel(new() { ["Wso2:Authority"] = "iam.valoria.test/wso2" });

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<Wso2Options>>().Value)
            .Message.ShouldContain("Wso2:Authority must be an absolute https URL");
    }

    [Fact]
    public void Backchannel_registration_refuses_a_missing_authority_and_names_the_setting()
    {
        using var provider = BuildBackchannel([]);

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<Wso2Options>>().Value)
            .Message.ShouldContain("Wso2:Authority must be an absolute https URL");
    }

    [Fact]
    public void Backchannel_registration_refuses_a_trusted_ca_file_that_does_not_exist()
    {
        using var provider = BuildBackchannel(new()
        {
            ["Wso2:Authority"] = "https://iam.valoria.test/",
            ["Wso2:TrustedCaPath"] = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pem"),
        });

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<Wso2Options>>().Value)
            .Message.ShouldContain("TrustedCaPath");
    }

    private static Wso2Options Options(string authority) => new() { Authority = new Uri(authority) };

    private static ServiceProvider BuildBackchannel(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddLogging().AddWso2Backchannel(configuration).BuildServiceProvider();
    }
}
