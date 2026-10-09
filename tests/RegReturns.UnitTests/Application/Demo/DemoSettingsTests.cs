using Microsoft.Extensions.Configuration;

using RegReturns.Application.Demo;

namespace RegReturns.UnitTests.Application.Demo;

public sealed class DemoSettingsTests
{
    [Theory]
    [InlineData("approver.mfa", "APPROVER_MFA")]
    [InlineData("maker.hlb", "MAKER_HLB")]
    [InlineData("admin-demo", "ADMIN_DEMO")]
    [InlineData("auditor", "AUDITOR")]
    public void Setting_suffix_is_the_user_name_in_upper_case_with_underscores(string userName, string expected)
    {
        DemoAccounts.SettingSuffix(userName).ShouldBe(expected);
    }

    [Fact]
    public void Totp_secret_is_found_by_user_name_whatever_the_key_case()
    {
        var options = new DemoOptions();
        options.TotpSecrets["approver_mfa"] = " JBSWY3DPEHPK3PXPJBSWY3DP ";

        options.TotpSecretFor("approver.mfa").ShouldBe("JBSWY3DPEHPK3PXPJBSWY3DP");
    }

    [Fact]
    public void An_account_without_a_configured_secret_has_none()
    {
        var options = new DemoOptions();
        options.TotpSecrets["APPROVER_MFA"] = "   ";

        options.TotpSecretFor("approver.mfa").ShouldBeNull();
        options.TotpSecretFor("reviewer").ShouldBeNull();
    }

    [Fact]
    public void Settings_bind_from_configuration_with_the_secrets_by_suffix()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Demo:Enabled"] = "true",
                ["Demo:UserPassword"] = "demo-password",
                ["Demo:TotpSecrets:APPROVER_MFA"] = "JBSWY3DPEHPK3PXPJBSWY3DP",
                ["Demo:ResetSchedule"] = "0 3 * * *",
            })
            .Build();

        var options = configuration.GetSection(DemoOptions.SectionName).Get<DemoOptions>()!;

        options.Enabled.ShouldBeTrue();
        options.UserPassword.ShouldBe("demo-password");
        options.TotpSecretFor("approver.mfa").ShouldBe("JBSWY3DPEHPK3PXPJBSWY3DP");
        options.ResetCooldownMinutes.ShouldBe(10);
    }

    [Fact]
    public void Demo_mode_is_off_unless_configured()
    {
        new DemoOptions().Enabled.ShouldBeFalse();
    }
}
