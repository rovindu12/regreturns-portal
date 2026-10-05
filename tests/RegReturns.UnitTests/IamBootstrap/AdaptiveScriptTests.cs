extern alias IamBootstrapTool;

using System.Globalization;

using IamBootstrapTool::RegReturns.IamBootstrap.Steps;

using RegReturns.Application.Identity;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class AdaptiveScriptTests
{
    [Fact]
    public void Script_asks_approvers_and_administrators_for_totp()
    {
        var script = PortalAppStep.AdaptiveScript(enforceMfa: true, []);

        script.ShouldContain($"var enforceForRoles = [\"{RoleNames.SupervisorApprover}\",\"{RoleNames.SystemAdmin}\"];");
    }

    [Fact]
    public void Script_does_not_ask_other_roles_for_totp()
    {
        var script = PortalAppStep.AdaptiveScript(enforceMfa: true, []);

        foreach (var role in new[] { RoleNames.BankMaker, RoleNames.BankChecker, RoleNames.SupervisorReviewer, RoleNames.Auditor })
        {
            script.ShouldNotContain($"\"{role}\"");
        }
    }

    [Fact]
    public void Enforced_mfa_is_switched_on_in_the_script()
    {
        PortalAppStep.AdaptiveScript(enforceMfa: true, []).ShouldContain("var enforce = true;");
    }

    [Fact]
    public void Mfa_can_be_switched_off_for_roles()
    {
        PortalAppStep.AdaptiveScript(enforceMfa: false, []).ShouldContain("var enforce = false;");
    }

    [Fact]
    public void Always_mfa_users_are_listed()
    {
        var script = PortalAppStep.AdaptiveScript(enforceMfa: false, ["approver.mfa", "admin.mfa"]);

        script.ShouldContain("var alwaysUsers = [\"approver.mfa\",\"admin.mfa\"];");
    }

    [Fact]
    public void Always_mfa_users_are_listed_once_whatever_their_case()
    {
        var script = PortalAppStep.AdaptiveScript(enforceMfa: true, ["approver.mfa", "Approver.MFA", "admin.mfa", "approver.mfa"]);

        script.ShouldContain("var alwaysUsers = [\"approver.mfa\",\"admin.mfa\"];");
    }

    [Fact]
    public void Always_mfa_users_get_totp_even_when_role_mfa_is_off()
    {
        var script = PortalAppStep.AdaptiveScript(enforceMfa: false, ["approver.mfa"]);

        // The user check is not guarded by the enforce flag; only the role check is.
        script.ShouldContain("alwaysUsers.indexOf(user.username) >= 0 ||");
        script.ShouldContain("(enforce && hasAnyOfTheRolesV2(context, enforceForRoles))");
    }

    [Fact]
    public void Empty_always_mfa_list_is_an_empty_array()
    {
        PortalAppStep.AdaptiveScript(enforceMfa: true, []).ShouldContain("var alwaysUsers = [];");
    }

    [Fact]
    public void User_names_cannot_break_out_of_the_script_string()
    {
        var script = PortalAppStep.AdaptiveScript(enforceMfa: true, ["x\"];executeStep(9);//"]);

        script.ShouldContain("var alwaysUsers = [\"x\\u0022];executeStep(9);//\"];");
        script.ShouldNotContain("x\"]");
    }

    [Fact]
    public void Totp_is_the_second_step_after_the_first_succeeds()
    {
        var script = PortalAppStep.AdaptiveScript(enforceMfa: true, []);

        script.ShouldContain("executeStep(1, {");
        script.ShouldContain("executeStep(2);");
    }

    [Fact]
    public void Script_is_the_same_in_every_culture()
    {
        var invariant = PortalAppStep.AdaptiveScript(enforceMfa: true, ["approver.mfa"]);
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            PortalAppStep.AdaptiveScript(enforceMfa: true, ["approver.mfa"]).ShouldBe(invariant);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
