using System.Security.Claims;

using RegReturns.Application.Identity;

namespace RegReturns.UnitTests.Authorization;

public sealed class FallbackPolicyTests
{
    [Fact]
    public async Task Fallback_policy_rejects_anonymous_callers()
    {
        (await PolicyHarness.FallbackAllowsAsync(PolicyHarness.Anonymous())).ShouldBeFalse();
    }

    [Fact]
    public async Task Fallback_policy_rejects_claims_without_authentication()
    {
        var unauthenticated = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimNames.Subject, "u1")]));

        (await PolicyHarness.FallbackAllowsAsync(unauthenticated)).ShouldBeFalse();
    }

    [Fact]
    public async Task Fallback_policy_admits_any_signed_in_user()
    {
        (await PolicyHarness.FallbackAllowsAsync(PolicyHarness.Authenticated((ClaimNames.Subject, "u1")))).ShouldBeTrue();
    }
}
