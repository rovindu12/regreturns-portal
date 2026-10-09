using RegReturns.Application.Demo;
using RegReturns.Domain.Identity;
using RegReturns.Web.Models.Demo;

namespace RegReturns.UnitTests.Web.Demo;

public sealed class DemoPageViewModelTests
{
    private static readonly DemoStatus Status = new(true, [], null, null, 10);

    private static readonly DemoAccount[] Accounts =
    [
        new("checker.hlb", "Harbourline Checker", Role.BankChecker, "HLB", "Harbourline Bank PLC"),
        new("maker.hlb", "Harbourline Maker", Role.BankMaker, "HLB", "Harbourline Bank PLC"),
        new("approver.mfa", "Approver With MFA", Role.SupervisorApprover, null, null),
    ];

    [Fact]
    public void Groups_accounts_by_role_in_workflow_order_and_leaves_out_empty_roles()
    {
        var page = DemoPageViewModel.Create(Accounts, new DemoOptions(), null, Status);

        page.Roles.Select(g => g.Role.Role).ShouldBe([Role.BankMaker, Role.BankChecker, Role.SupervisorApprover]);
        page.Roles[0].Accounts.ShouldHaveSingleItem().Bank.ShouldBe("Harbourline Bank PLC");
    }

    [Fact]
    public void Only_accounts_with_a_configured_secret_get_a_qr_code()
    {
        var options = new DemoOptions();
        options.TotpSecrets["APPROVER_MFA"] = "JBSWY3DPEHPK3PXPJBSWY3DP";

        var cards = DemoPageViewModel.Create(Accounts, options, null, Status).Roles.SelectMany(g => g.Accounts).ToList();

        var approver = cards.Single(c => c.UserName == "approver.mfa");
        approver.TotpSecret.ShouldBe("JBSWY3DPEHPK3PXPJBSWY3DP");
        approver.TotpQrSvg.ShouldNotBeNull();
        cards.Where(c => c.UserName != "approver.mfa").ShouldAllBe(c => c.TotpSecret == null && c.TotpQrSvg == null);
    }

    [Fact]
    public void The_api_client_is_read_only_and_links_to_swagger()
    {
        var options = new DemoOptions
        {
            ApiClientId = "demo-client",
            ApiClientSecret = " demo-secret ",
            ApiBaseUrl = new Uri("https://api.demo.example/"),
        };
        var token = new Uri("https://iam.demo.example/oauth2/token");

        var api = DemoPageViewModel.Create(Accounts, options, token, Status).Api.ShouldNotBeNull();

        api.ClientSecret.ShouldBe("demo-secret");
        api.Scopes.ShouldBe(["reference:read", "returns:read"]);
        api.SwaggerUrl.ShouldBe(new Uri("https://api.demo.example/swagger"));
        api.TokenEndpoint.ShouldBe(token);
    }

    [Fact]
    public void Nothing_is_published_that_is_not_configured()
    {
        var page = DemoPageViewModel.Create(Accounts, new DemoOptions { UserPassword = "  " }, null, Status);

        page.Password.ShouldBeNull();
        page.Api.ShouldBeNull();
    }
}
