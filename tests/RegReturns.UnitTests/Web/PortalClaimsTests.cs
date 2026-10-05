using System.Security.Claims;

using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;
using RegReturns.Web.Identity;

namespace RegReturns.UnitTests.Web;

public sealed class PortalClaimsTests
{
    [Fact]
    public void Single_role_sent_as_a_string_is_kept()
    {
        var result = PortalClaims.Normalize(Principal((ClaimNames.Roles, RoleNames.BankMaker)));

        result.Roles.ShouldBe([Role.BankMaker]);
    }

    [Fact]
    public void Several_roles_sent_as_an_array_are_all_kept()
    {
        var result = PortalClaims.Normalize(Principal((ClaimNames.Roles, RoleNames.Auditor), (ClaimNames.Roles, RoleNames.SystemAdmin)));

        result.Roles.ShouldBe([Role.SystemAdmin, Role.Auditor]);
    }

    [Fact]
    public void Space_separated_roles_are_split()
    {
        var result = PortalClaims.Normalize(Principal((ClaimNames.Roles, $"{RoleNames.SupervisorReviewer} {RoleNames.SupervisorApprover}")));

        result.Roles.ShouldBe([Role.SupervisorReviewer, Role.SupervisorApprover]);
    }

    [Fact]
    public void Comma_separated_roles_are_split()
    {
        var result = PortalClaims.Normalize(Principal((ClaimNames.Roles, $"{RoleNames.BankMaker}, {RoleNames.BankChecker}")));

        result.Roles.ShouldBe([Role.BankMaker, Role.BankChecker]);
    }

    [Fact]
    public void Roles_sent_as_a_JSON_array_string_are_parsed()
    {
        var result = PortalClaims.Normalize(Principal((ClaimNames.Roles, $"[\"{RoleNames.BankChecker}\",\"{RoleNames.BankMaker}\"]")));

        result.Roles.ShouldBe([Role.BankMaker, Role.BankChecker]);
    }

    [Fact]
    public void Unknown_roles_are_dropped_and_reported()
    {
        var result = PortalClaims.Normalize(Principal(
            (ClaimNames.Roles, RoleNames.BankMaker), (ClaimNames.Roles, "Internal/everyone"), (ClaimNames.Roles, "superuser")));

        result.Roles.ShouldBe([Role.BankMaker]);
        result.UnknownRoles.ShouldBe(["Internal/everyone", "superuser"]);
        result.Principal.FindAll(ClaimNames.Roles).Select(c => c.Value).ShouldBe([RoleNames.BankMaker]);
    }

    [Fact]
    public void Name_claim_is_used_as_the_display_name()
    {
        var result = PortalClaims.Normalize(Principal(
            (ClaimNames.Name, "Maker (Harbourline Bank PLC)"), ("given_name", "Ignored"), (ClaimNames.UserName, "maker")));

        result.Name.ShouldBe("Maker (Harbourline Bank PLC)");
        result.Principal.Identity!.Name.ShouldBe("Maker (Harbourline Bank PLC)");
    }

    [Fact]
    public void Name_falls_back_to_given_and_family_name()
    {
        var result = PortalClaims.Normalize(Principal(("given_name", "Ada"), ("family_name", "Okafor"), (ClaimNames.UserName, "ada")));

        result.Name.ShouldBe("Ada Okafor");
    }

    [Fact]
    public void Name_falls_back_to_the_user_name()
    {
        var result = PortalClaims.Normalize(Principal((ClaimNames.UserName, "checker")));

        result.Name.ShouldBe("checker");
    }

    [Fact]
    public void Only_the_claims_the_portal_needs_are_kept()
    {
        var result = PortalClaims.Normalize(Principal(
            (ClaimNames.Subject, "8d0c7b3e-0000-4000-8000-000000000001"),
            (ClaimNames.UserName, "maker"),
            (ClaimNames.Email, "maker@harbourline.example"),
            (ClaimNames.InstitutionId, "HBL"),
            (ClaimNames.Roles, RoleNames.BankMaker),
            (ClaimNames.AuthenticationMethods, "BasicAuthenticator"),
            (ClaimNames.AuthenticationMethods, "TOTP"),
            (ClaimNames.SessionId, "sid-1"),
            ("aud", "regreturns-portal"),
            ("aud", "https://api.regreturns"),
            ("azp", "regreturns-portal"),
            ("nonce", "n-1"),
            ("at_hash", "h"),
            ("iss", "https://localhost:9443/oauth2/token")));

        result.Principal.Claims.Select(c => c.Type).Distinct().ShouldBe(
        [
            ClaimNames.Subject, ClaimNames.UserName, ClaimNames.Name, ClaimNames.Email, ClaimNames.Roles,
            ClaimNames.InstitutionId, ClaimNames.AuthenticationMethods, ClaimNames.SessionId,
        ]);
        result.AuthenticationMethods.ShouldBe(["BasicAuthenticator", "TOTP"]);
    }

    [Fact]
    public void Normalised_identity_uses_raw_name_and_role_claim_types()
    {
        var result = PortalClaims.Normalize(Principal((ClaimNames.UserName, "maker"), (ClaimNames.Roles, RoleNames.BankMaker)));

        result.Principal.IsInRole(RoleNames.BankMaker).ShouldBeTrue();
        result.Principal.Identity!.IsAuthenticated.ShouldBeTrue();
    }

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "oidc"));
}
