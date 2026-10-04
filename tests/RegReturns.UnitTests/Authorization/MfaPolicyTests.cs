using System.Security.Claims;

using RegReturns.Application.Authorization;
using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;

namespace RegReturns.UnitTests.Authorization;

public sealed class MfaPolicyTests
{
    public static TheoryData<string, Role> StepUpPolicies() => new()
    {
        { Policies.SupervisionApprove, Role.SupervisorApprover },
        { Policies.AdminManage, Role.SystemAdmin },
    };

    [Theory]
    [MemberData(nameof(StepUpPolicies))]
    public async Task Policy_requires_totp_when_mfa_is_enforced(string policy, Role role)
    {
        var allowed = await PolicyHarness.AllowsAsync(policy, PolicyHarness.Person(role, withTotp: false), PolicyHarness.Mfa(enforce: true));

        allowed.ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(StepUpPolicies))]
    public async Task Policy_allows_a_totp_sign_in_when_mfa_is_enforced(string policy, Role role)
    {
        var allowed = await PolicyHarness.AllowsAsync(policy, PolicyHarness.Person(role, withTotp: true), PolicyHarness.Mfa(enforce: true));

        allowed.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(StepUpPolicies))]
    public async Task Policy_allows_a_password_sign_in_when_mfa_is_off(string policy, Role role)
    {
        var allowed = await PolicyHarness.AllowsAsync(policy, PolicyHarness.Person(role, withTotp: false), PolicyHarness.Mfa(enforce: false));

        allowed.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(StepUpPolicies))]
    public async Task Mfa_is_enforced_when_not_configured(string policy, Role role)
    {
        (await PolicyHarness.AllowsAsync(policy, PolicyHarness.Person(role, withTotp: false))).ShouldBeFalse();
    }

    [Fact]
    public async Task Turning_mfa_off_does_not_grant_the_policy_to_other_roles()
    {
        var allowed = await PolicyHarness.AllowsAsync(
            Policies.SupervisionApprove, PolicyHarness.Person(Role.SupervisorReviewer, withTotp: false), PolicyHarness.Mfa(enforce: false));

        allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task Totp_method_is_recognised_case_insensitively()
    {
        var user = Approver((ClaimNames.AuthenticationMethods, "TOTP"));

        (await PolicyHarness.AllowsAsync(Policies.SupervisionApprove, user)).ShouldBeTrue();
    }

    [Theory]
    [InlineData("pwd")]
    [InlineData("otp")]
    [InlineData("mfa")]
    [InlineData("totp-pending")]
    public async Task Other_authentication_methods_do_not_count_as_totp(string method)
    {
        var user = Approver((ClaimNames.AuthenticationMethods, method));

        (await PolicyHarness.AllowsAsync(Policies.SupervisionApprove, user)).ShouldBeFalse();
    }

    [Fact]
    public async Task Configured_authentication_methods_are_accepted()
    {
        var settings = new Dictionary<string, string?> { ["Iam:EnforceMfa"] = "true", ["Iam:MfaAuthenticationMethods:0"] = "otp" };

        (await PolicyHarness.AllowsAsync(Policies.SupervisionApprove, Approver((ClaimNames.AuthenticationMethods, "otp")), settings)).ShouldBeTrue();
    }

    [Fact]
    public async Task Review_does_not_require_totp()
    {
        var allowed = await PolicyHarness.AllowsAsync(
            Policies.SupervisionReview, PolicyHarness.Person(Role.SupervisorReviewer, withTotp: false), PolicyHarness.Mfa(enforce: true));

        allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Audit_read_does_not_require_totp()
    {
        var allowed = await PolicyHarness.AllowsAsync(
            Policies.AuditRead, PolicyHarness.Person(Role.Auditor, withTotp: false), PolicyHarness.Mfa(enforce: true));

        allowed.ShouldBeTrue();
    }

    private static ClaimsPrincipal Approver((string Type, string Value) amr) =>
        PolicyHarness.Authenticated((ClaimNames.Subject, "approver"), (ClaimNames.Roles, RoleNames.SupervisorApprover), amr);
}
