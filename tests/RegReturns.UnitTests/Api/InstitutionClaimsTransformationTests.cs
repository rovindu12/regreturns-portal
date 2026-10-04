using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using RegReturns.Api.Authentication;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;

namespace RegReturns.UnitTests.Api;

public sealed class InstitutionClaimsTransformationTests : IDisposable
{
    private const string RegisteredClient = "regreturns-bank-hlb";
    private const string UnknownClient = "regreturns-bank-zzz";
    private static readonly ApiClientInstitution Harbourline = new(Guid.CreateVersion7(), "HLB");

    private readonly IQueryHandler<GetApiClientInstitution, ApiClientInstitution?> _lookup =
        Substitute.For<IQueryHandler<GetApiClientInstitution, ApiClientInstitution?>>();

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly InstitutionClaimsTransformation _transformation;

    public InstitutionClaimsTransformationTests()
    {
        _lookup.HandleAsync(Arg.Any<GetApiClientInstitution>(), Arg.Any<CancellationToken>()).Returns((ApiClientInstitution?)null);
        _lookup.HandleAsync(Arg.Is<GetApiClientInstitution>(q => q.ClientId == RegisteredClient), Arg.Any<CancellationToken>())
            .Returns(Harbourline);
        _transformation = new InstitutionClaimsTransformation(
            _lookup, _cache, new HttpContextAccessor(), NullLogger<InstitutionClaimsTransformation>.Instance);
    }

    [Fact]
    public async Task Registered_client_gets_its_institution_code_and_key()
    {
        var result = await _transformation.TransformAsync(ClientPrincipal(RegisteredClient));

        result.FindFirst(ClaimNames.InstitutionId)!.Value.ShouldBe("HLB");
        result.FindFirst(ApiClaimNames.InstitutionKey)!.Value.ShouldBe(Harbourline.InstitutionId.ToString());
    }

    [Fact]
    public async Task Added_claims_are_marked_as_issued_by_the_api()
    {
        var result = await _transformation.TransformAsync(ClientPrincipal(RegisteredClient));

        result.FindAll(c => c.Type is ClaimNames.InstitutionId or ApiClaimNames.InstitutionKey)
            .ShouldAllBe(c => c.Issuer == ApiClaimNames.Issuer);
    }

    [Fact]
    public async Task Unregistered_client_gets_no_institution()
    {
        var result = await _transformation.TransformAsync(ClientPrincipal(UnknownClient));

        result.HasClaim(c => c.Type is ClaimNames.InstitutionId or ApiClaimNames.InstitutionKey).ShouldBeFalse();
    }

    [Fact]
    public async Task Client_id_claim_is_used_when_azp_is_absent()
    {
        var principal = Principal(
            (ClaimNames.AuthorizedUserType, ClaimNames.ApplicationTokenType), (ClaimNames.ClientId, RegisteredClient));

        var result = await _transformation.TransformAsync(principal);

        result.FindFirst(ClaimNames.InstitutionId)!.Value.ShouldBe("HLB");
    }

    [Fact]
    public async Task User_tokens_are_never_looked_up()
    {
        var principal = Principal((ClaimNames.AuthorizedUserType, "APPLICATION_USER"), (ClaimNames.AuthorizedParty, RegisteredClient));

        await _transformation.TransformAsync(principal);

        await _lookup.DidNotReceiveWithAnyArgs().HandleAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Institution_claims_carried_in_a_token_are_dropped()
    {
        var principal = Principal(
            (ClaimNames.AuthorizedUserType, "APPLICATION_USER"),
            (ClaimNames.Subject, "user-1"),
            (ClaimNames.InstitutionId, "HLB"),
            (ApiClaimNames.InstitutionKey, Guid.NewGuid().ToString()));

        var result = await _transformation.TransformAsync(principal);

        result.HasClaim(c => c.Type is ClaimNames.InstitutionId or ApiClaimNames.InstitutionKey).ShouldBeFalse();
    }

    [Fact]
    public async Task A_token_institution_is_replaced_by_the_registered_one()
    {
        var principal = ClientPrincipal(RegisteredClient, (ClaimNames.InstitutionId, "CCB"));

        var result = await _transformation.TransformAsync(principal);

        result.FindAll(ClaimNames.InstitutionId).Select(c => c.Value).ShouldBe(["HLB"]);
    }

    [Fact]
    public async Task The_authenticated_principal_is_not_changed_in_place()
    {
        var principal = ClientPrincipal(RegisteredClient);

        await _transformation.TransformAsync(principal);

        principal.HasClaim(c => c.Type == ClaimNames.InstitutionId).ShouldBeFalse();
    }

    [Fact]
    public async Task Transforming_a_transformed_principal_adds_nothing()
    {
        var once = await _transformation.TransformAsync(ClientPrincipal(RegisteredClient));

        var twice = await _transformation.TransformAsync(once);

        twice.FindAll(ClaimNames.InstitutionId).Count().ShouldBe(1);
        twice.FindAll(ApiClaimNames.InstitutionKey).Count().ShouldBe(1);
    }

    [Fact]
    public async Task Lookups_are_cached_including_misses()
    {
        await _transformation.TransformAsync(ClientPrincipal(UnknownClient));
        await _transformation.TransformAsync(ClientPrincipal(UnknownClient));

        await _lookup.Received(1).HandleAsync(Arg.Is<GetApiClientInstitution>(q => q.ClientId == UnknownClient), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Anonymous_principals_are_returned_unchanged()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await _transformation.TransformAsync(anonymous);

        result.ShouldBeSameAs(anonymous);
    }

    public void Dispose() => _cache.Dispose();

    private static ClaimsPrincipal ClientPrincipal(string clientId, params (string Type, string Value)[] extra) =>
        Principal(
        [
            (ClaimNames.AuthorizedUserType, ClaimNames.ApplicationTokenType),
            (ClaimNames.Subject, clientId),
            (ClaimNames.AuthorizedParty, clientId),
            (ClaimNames.ClientId, clientId),
            (ClaimNames.Scope, ApiScopes.ReferenceRead),
            .. extra,
        ]);

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "Bearer", ClaimNames.Subject, ClaimNames.Roles));
}
