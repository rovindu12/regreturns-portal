extern alias IamBootstrapTool;

using System.ComponentModel.DataAnnotations;

using IamBootstrapTool::RegReturns.IamBootstrap;

using Microsoft.Extensions.Configuration;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class BootstrapOptionsTests
{
    [Theory]
    [InlineData("https://localhost:7101")]
    [InlineData("https://localhost:7101/")]
    public void Redirect_url_is_the_oidc_callback_of_the_portal(string portal)
    {
        Options(portal).RedirectUrl.AbsoluteUri.ShouldBe("https://localhost:7101/signin-oidc");
    }

    [Theory]
    [InlineData("https://localhost:7101")]
    [InlineData("https://localhost:7101/")]
    public void Post_logout_redirect_url_is_the_signout_callback_of_the_portal(string portal)
    {
        Options(portal).PostLogoutRedirectUrl.AbsoluteUri.ShouldBe("https://localhost:7101/signout-callback-oidc");
    }

    [Theory]
    [InlineData("https://localhost:7101")]
    [InlineData("https://localhost:7101/")]
    public void Backchannel_logout_url_defaults_to_the_portal(string portal)
    {
        Options(portal).EffectiveBackchannelLogoutUrl.AbsoluteUri.ShouldBe("https://localhost:7101/signout-backchannel");
    }

    [Theory]
    [InlineData("https://portal.valoria.test/regreturns")]
    [InlineData("https://portal.valoria.test/regreturns/")]
    public void Derived_urls_keep_the_base_path_of_the_portal(string portal)
    {
        var options = Options(portal);

        options.RedirectUrl.AbsoluteUri.ShouldBe("https://portal.valoria.test/regreturns/signin-oidc");
        options.PostLogoutRedirectUrl.AbsoluteUri.ShouldBe("https://portal.valoria.test/regreturns/signout-callback-oidc");
        options.EffectiveBackchannelLogoutUrl.AbsoluteUri.ShouldBe("https://portal.valoria.test/regreturns/signout-backchannel");
    }

    [Fact]
    public void Configured_backchannel_logout_url_wins()
    {
        var options = Options("https://localhost:7101/");
        options.BackchannelLogoutUrl = new Uri("https://web:8443/signout-backchannel");

        options.EffectiveBackchannelLogoutUrl.AbsoluteUri.ShouldBe("https://web:8443/signout-backchannel");
    }

    [Fact]
    public void Derived_urls_need_the_portal_base_url()
    {
        Should.Throw<InvalidOperationException>(() => new BootstrapOptions().RedirectUrl)
            .Message.ShouldContain("IamBootstrap:PortalBaseUrl");
    }

    [Fact]
    public void Complete_settings_are_valid()
    {
        Validate(Valid()).ShouldBeEmpty();
    }

    [Fact]
    public void Demo_password_shorter_than_twelve_characters_is_rejected()
    {
        var options = Valid();
        options.DemoUserPassword = new string('x', BootstrapOptions.MinimumDemoPasswordLength - 1);

        Validate(options).ShouldHaveSingleItem().MemberNames.ShouldBe([nameof(BootstrapOptions.DemoUserPassword)]);
    }

    [Theory]
    [InlineData(nameof(BootstrapOptions.AdminUserName))]
    [InlineData(nameof(BootstrapOptions.AdminPassword))]
    [InlineData(nameof(BootstrapOptions.DemoUserPassword))]
    [InlineData(nameof(BootstrapOptions.PortalBaseUrl))]
    public void Required_setting_is_enforced(string property)
    {
        var options = Valid();
        typeof(BootstrapOptions).GetProperty(property)!.SetValue(options, null);

        Validate(options).ShouldHaveSingleItem().MemberNames.ShouldBe([property]);
    }

    [Fact]
    public void Mfa_is_enforced_by_default()
    {
        new BootstrapOptions().EnforceMfa.ShouldBeTrue();
    }

    [Fact]
    public void No_user_always_gets_mfa_by_default()
    {
        new BootstrapOptions().MfaAlwaysUsers.ShouldBeEmpty();
    }

    [Fact]
    public void Mfa_always_users_bound_from_configuration_are_listed_once()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{BootstrapOptions.SectionName}:MfaAlwaysUsers:0"] = "approver.mfa" })
            .Build();
        var options = new BootstrapOptions();

        configuration.GetSection(BootstrapOptions.SectionName).Bind(options);

        options.MfaAlwaysUsers.ShouldBe(["approver.mfa"]);
    }

    [Fact]
    public void Self_service_is_locked_down_by_default()
    {
        new BootstrapOptions().LockDownSelfService.ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://localhost:7201/", "https://localhost:7201")]
    [InlineData("https://api.valoria.test/regreturns/", "https://api.valoria.test")]
    public void Api_origin_is_the_scheme_host_and_port_of_the_api(string api, string origin)
    {
        new BootstrapOptions { ApiBaseUrl = new Uri(api) }.ApiOrigin.ShouldBe(origin);
    }

    [Fact]
    public void Without_an_api_address_there_is_no_origin()
    {
        new BootstrapOptions().ApiOrigin.ShouldBeNull();
    }

    private static BootstrapOptions Options(string portal) => new() { PortalBaseUrl = new Uri(portal) };

    private static BootstrapOptions Valid() => new()
    {
        AdminUserName = "iamadmin",
        AdminPassword = "admin-password",
        PortalBaseUrl = new Uri("https://localhost:7101/"),
        DemoUserPassword = "demo-password-123",
    };

    private static List<ValidationResult> Validate(BootstrapOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
