using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

using RegReturns.Application.Authorization;
using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;

namespace RegReturns.UnitTests.Authorization;

public sealed class RolePolicyMatrixTests
{
    /// <summary>The roles each policy grants to a signed-in person (bank roles with an institution, TOTP done).</summary>
    private static readonly Dictionary<string, Role[]> Granted = new()
    {
        [Policies.BankAccess] = [Role.BankMaker, Role.BankChecker],
        [Policies.BankPrepareReturn] = [Role.BankMaker],
        [Policies.BankSubmitReturn] = [Role.BankChecker],
        [Policies.SupervisionAccess] = [Role.SupervisorReviewer, Role.SupervisorApprover],
        [Policies.SupervisionReview] = [Role.SupervisorReviewer],
        [Policies.SupervisionApprove] = [Role.SupervisorApprover],
        [Policies.AdminManage] = [Role.SystemAdmin],
        [Policies.AuditRead] = [Role.Auditor, Role.SystemAdmin],
        [Policies.ReportsView] = Enum.GetValues<Role>(),

        // API policies are for client-credentials tokens only: no person qualifies by role.
        [Policies.ApiReturnsRead] = [],
        [Policies.ApiReturnsSubmit] = [],
        [Policies.ApiReferenceRead] = [],
    };

    public static TheoryData<Role, string, bool> Matrix()
    {
        var data = new TheoryData<Role, string, bool>();
        foreach (var (policy, roles) in Granted)
        {
            foreach (var role in Enum.GetValues<Role>())
            {
                data.Add(role, policy, roles.Contains(role));
            }
        }

        return data;
    }

    public static TheoryData<string> AllPolicies() => [.. PolicyHarness.AllPolicies];

    [Fact]
    public void Matrix_covers_every_declared_policy()
    {
        Granted.Keys.ShouldBe(PolicyHarness.AllPolicies, ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(AllPolicies))]
    public async Task Every_declared_policy_is_registered(string policy)
    {
        await using var provider = PolicyHarness.Build(settings: null);

        (await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(policy)).ShouldNotBeNull();
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Role_is_granted_exactly_the_policies_it_needs(Role role, string policy, bool allowed)
    {
        (await PolicyHarness.AllowsAsync(policy, PolicyHarness.Person(role))).ShouldBe(allowed);
    }

    [Theory]
    [MemberData(nameof(AllPolicies))]
    public async Task Every_policy_rejects_anonymous_callers(string policy)
    {
        (await PolicyHarness.AllowsAsync(policy, PolicyHarness.Anonymous())).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(AllPolicies))]
    public async Task Signed_in_person_without_roles_is_granted_nothing(string policy)
    {
        var user = PolicyHarness.Authenticated(
            (ClaimNames.Subject, "no-roles"), (ClaimNames.InstitutionId, PolicyHarness.Institution), (ClaimNames.AuthenticationMethods, "totp"));

        (await PolicyHarness.AllowsAsync(policy, user)).ShouldBeFalse();
    }

    [Fact]
    public async Task Unknown_role_names_grant_nothing()
    {
        var user = PolicyHarness.Authenticated(
            (ClaimNames.Subject, "x"), (ClaimNames.Roles, "Supervisor_Approver"), (ClaimNames.Roles, "admin"), (ClaimNames.AuthenticationMethods, "totp"));

        (await PolicyHarness.AllowsAsync(Policies.SupervisionApprove, user)).ShouldBeFalse();
    }
}
