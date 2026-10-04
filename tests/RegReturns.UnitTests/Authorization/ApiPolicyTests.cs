using System.Security.Claims;

using RegReturns.Application.Authorization;
using RegReturns.Application.Identity;

namespace RegReturns.UnitTests.Authorization;

public sealed class ApiPolicyTests
{
    private const string AllScopes = $"{ApiScopes.ReturnsRead} {ApiScopes.ReturnsSubmit} {ApiScopes.ReferenceRead}";

    public static TheoryData<string, string> ApiPolicies() => new()
    {
        { Policies.ApiReturnsRead, ApiScopes.ReturnsRead },
        { Policies.ApiReturnsSubmit, ApiScopes.ReturnsSubmit },
        { Policies.ApiReferenceRead, ApiScopes.ReferenceRead },
    };

    [Theory]
    [MemberData(nameof(ApiPolicies))]
    public async Task Registered_client_with_the_scope_is_allowed(string policy, string scope)
    {
        var client = Client((ClaimNames.ClientId, PolicyHarness.ClientId), (ClaimNames.Scope, scope), (ClaimNames.InstitutionId, PolicyHarness.Institution));

        (await PolicyHarness.AllowsAsync(policy, client)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(Policies.ApiReturnsRead)]
    [InlineData(Policies.ApiReturnsSubmit)]
    [InlineData(Policies.ApiReferenceRead)]
    public async Task Scope_is_found_among_several_space_separated_scopes(string policy)
    {
        var client = Client((ClaimNames.ClientId, PolicyHarness.ClientId), (ClaimNames.Scope, $"openid  {AllScopes} "), (ClaimNames.InstitutionId, PolicyHarness.Institution));

        (await PolicyHarness.AllowsAsync(policy, client)).ShouldBeTrue();
    }

    [Fact]
    public async Task Scopes_may_arrive_as_separate_claims()
    {
        var client = Client(
            (ClaimNames.ClientId, PolicyHarness.ClientId),
            (ClaimNames.Scope, ApiScopes.ReturnsRead),
            (ClaimNames.Scope, ApiScopes.ReturnsSubmit),
            (ClaimNames.InstitutionId, PolicyHarness.Institution));

        (await PolicyHarness.AllowsAsync(Policies.ApiReturnsSubmit, client)).ShouldBeTrue();
    }

    [Fact]
    public async Task Authorized_party_counts_as_the_client_id()
    {
        var client = Client((ClaimNames.AuthorizedParty, PolicyHarness.ClientId), (ClaimNames.Scope, ApiScopes.ReturnsRead), (ClaimNames.InstitutionId, PolicyHarness.Institution));

        (await PolicyHarness.AllowsAsync(Policies.ApiReturnsRead, client)).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(ApiPolicies))]
    public async Task Client_id_is_required(string policy, string scope)
    {
        var token = Client((ClaimNames.Subject, "someone"), (ClaimNames.Scope, scope), (ClaimNames.InstitutionId, PolicyHarness.Institution));

        (await PolicyHarness.AllowsAsync(policy, token)).ShouldBeFalse();
    }

    [Fact]
    public async Task Empty_client_id_is_not_a_client()
    {
        var token = Client((ClaimNames.ClientId, string.Empty), (ClaimNames.Scope, ApiScopes.ReturnsRead), (ClaimNames.InstitutionId, PolicyHarness.Institution));

        (await PolicyHarness.AllowsAsync(Policies.ApiReturnsRead, token)).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(ApiPolicies))]
    public async Task Scope_is_required(string policy, string scope)
    {
        var otherScopes = string.Join(' ', ApiScopes.All.Where(s => s != scope));
        var client = Client((ClaimNames.ClientId, PolicyHarness.ClientId), (ClaimNames.Scope, otherScopes), (ClaimNames.InstitutionId, PolicyHarness.Institution));

        (await PolicyHarness.AllowsAsync(policy, client)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("returns:readonly")]
    [InlineData("returns:read:all")]
    [InlineData("RETURNS:READ")]
    [InlineData("returns")]
    public async Task Scope_must_match_exactly(string grantedScope)
    {
        var client = Client((ClaimNames.ClientId, PolicyHarness.ClientId), (ClaimNames.Scope, grantedScope), (ClaimNames.InstitutionId, PolicyHarness.Institution));

        (await PolicyHarness.AllowsAsync(Policies.ApiReturnsRead, client)).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(ApiPolicies))]
    public async Task Institution_is_required(string policy, string scope)
    {
        var client = Client((ClaimNames.ClientId, PolicyHarness.ClientId), (ClaimNames.Scope, scope));

        (await PolicyHarness.AllowsAsync(policy, client)).ShouldBeFalse();
    }

    [Fact]
    public async Task Client_token_does_not_satisfy_portal_policies()
    {
        var client = Client((ClaimNames.ClientId, PolicyHarness.ClientId), (ClaimNames.Scope, AllScopes), (ClaimNames.InstitutionId, PolicyHarness.Institution));

        (await PolicyHarness.AllowsAsync(Policies.BankAccess, client)).ShouldBeFalse();
    }

    private static ClaimsPrincipal Client(params (string Type, string Value)[] claims) =>
        PolicyHarness.Authenticated([(ClaimNames.AuthorizedUserType, ClaimNames.ApplicationTokenType), .. claims]);
}
