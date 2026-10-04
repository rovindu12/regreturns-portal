using RegReturns.Application.Authorization;
using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;

namespace RegReturns.UnitTests.Authorization;

public sealed class InstitutionPolicyTests
{
    public static TheoryData<string, Role> BankPolicies() => new()
    {
        { Policies.BankAccess, Role.BankMaker },
        { Policies.BankAccess, Role.BankChecker },
        { Policies.BankPrepareReturn, Role.BankMaker },
        { Policies.BankSubmitReturn, Role.BankChecker },
    };

    [Theory]
    [MemberData(nameof(BankPolicies))]
    public async Task Bank_policy_requires_an_institution(string policy, Role role)
    {
        var user = PolicyHarness.Authenticated((ClaimNames.Subject, "u1"), (ClaimNames.Roles, RoleNames.For(role)));

        (await PolicyHarness.AllowsAsync(policy, user)).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(BankPolicies))]
    public async Task Bank_policy_rejects_a_blank_institution(string policy, Role role)
    {
        var user = PolicyHarness.Authenticated(
            (ClaimNames.Subject, "u1"), (ClaimNames.Roles, RoleNames.For(role)), (ClaimNames.InstitutionId, "  "));

        (await PolicyHarness.AllowsAsync(policy, user)).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(BankPolicies))]
    public async Task Bank_policy_allows_the_role_with_an_institution(string policy, Role role)
    {
        var user = PolicyHarness.Authenticated(
            (ClaimNames.Subject, "u1"), (ClaimNames.Roles, RoleNames.For(role)), (ClaimNames.InstitutionId, PolicyHarness.Institution));

        (await PolicyHarness.AllowsAsync(policy, user)).ShouldBeTrue();
    }

    [Fact]
    public async Task Bank_policies_do_not_require_totp()
    {
        (await PolicyHarness.AllowsAsync(Policies.BankSubmitReturn, PolicyHarness.Person(Role.BankChecker, withTotp: false))).ShouldBeTrue();
    }

    [Fact]
    public async Task Regulator_policies_do_not_need_an_institution()
    {
        (await PolicyHarness.AllowsAsync(Policies.SupervisionReview, PolicyHarness.Person(Role.SupervisorReviewer))).ShouldBeTrue();
    }
}
