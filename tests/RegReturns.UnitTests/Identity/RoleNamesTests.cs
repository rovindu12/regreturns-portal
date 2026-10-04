using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;

namespace RegReturns.UnitTests.Identity;

public sealed class RoleNamesTests
{
    public static TheoryData<Role, string> Mapping() => new()
    {
        { Role.BankMaker, RoleNames.BankMaker },
        { Role.BankChecker, RoleNames.BankChecker },
        { Role.SupervisorReviewer, RoleNames.SupervisorReviewer },
        { Role.SupervisorApprover, RoleNames.SupervisorApprover },
        { Role.SystemAdmin, RoleNames.SystemAdmin },
        { Role.Auditor, RoleNames.Auditor },
    };

    [Fact]
    public void All_lists_the_six_wso2_roles_in_role_order()
    {
        RoleNames.All.ShouldBe(
        [
            RoleNames.BankMaker,
            RoleNames.BankChecker,
            RoleNames.SupervisorReviewer,
            RoleNames.SupervisorApprover,
            RoleNames.SystemAdmin,
            RoleNames.Auditor,
        ]);
    }

    [Fact]
    public void Every_role_has_a_wso2_name()
    {
        Enum.GetValues<Role>().Select(RoleNames.For).ShouldBe(RoleNames.All, ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(Mapping))]
    public void For_returns_the_wso2_role_name(Role role, string name)
    {
        RoleNames.For(role).ShouldBe(name);
    }

    [Theory]
    [MemberData(nameof(Mapping))]
    public void TryParse_maps_a_wso2_role_name_to_its_role(Role role, string name)
    {
        RoleNames.TryParse(name, out var parsed).ShouldBeTrue();
        parsed.ShouldBe(role);
    }

    [Theory]
    [MemberData(nameof(Mapping))]
    public void TryParse_round_trips_with_For(Role role, string name)
    {
        RoleNames.TryParse(RoleNames.For(role), out var parsed).ShouldBeTrue();
        RoleNames.For(parsed.Value).ShouldBe(name);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("Bank_Maker")]
    [InlineData("BANK_MAKER")]
    [InlineData(" bank_maker")]
    [InlineData("BankMaker")]
    [InlineData("everyone")]
    public void TryParse_rejects_unknown_names(string name)
    {
        RoleNames.TryParse(name, out var parsed).ShouldBeFalse();
        parsed.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_rejects_missing_names(string? name)
    {
        RoleNames.TryParse(name, out var parsed).ShouldBeFalse();
        parsed.ShouldBeNull();
    }

    [Fact]
    public void Api_scopes_list_every_scope_of_the_api_resource()
    {
        ApiScopes.All.ShouldBe([ApiScopes.ReturnsRead, ApiScopes.ReturnsSubmit, ApiScopes.ReferenceRead]);
    }
}
