using System.Security.Claims;

using RegReturns.Application.Auditing;
using RegReturns.Application.Identity;
using RegReturns.Domain.Auditing;

namespace RegReturns.UnitTests.Auditing;

public sealed class AuditActorTests
{
    private const string Subject = "3f2b9c1e-0d4a-4f6b-9e8c-2a7d5b1c0e9f";
    private const string ClientId = "alpha-core-banking";

    [Fact]
    public void Missing_principal_is_anonymous()
    {
        AuditActor.FromPrincipal(null).ShouldBe(AuditActor.Anonymous);
    }

    [Fact]
    public void Unauthenticated_principal_is_anonymous_even_with_a_subject()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimNames.Subject, Subject)]));

        AuditActor.FromPrincipal(principal).ShouldBe(AuditActor.Anonymous);
    }

    [Fact]
    public void Anonymous_actor_uses_the_anonymous_subject()
    {
        AuditActor.Anonymous.ShouldBe(new AuditActor(ActorType.Anonymous, AuditActor.AnonymousSubject, null, null));
    }

    [Fact]
    public void Signed_in_user_is_recorded_with_subject_name_and_institution()
    {
        var principal = Authenticated(
            (ClaimNames.Subject, Subject),
            (ClaimNames.Name, "Nadia Fernhill"),
            (ClaimNames.UserName, "maker.alpha"),
            (ClaimNames.InstitutionId, "ALPHA"));

        AuditActor.FromPrincipal(principal).ShouldBe(new AuditActor(ActorType.User, Subject, "Nadia Fernhill", "ALPHA"));
    }

    [Fact]
    public void User_without_a_name_is_recorded_with_the_user_name()
    {
        var principal = Authenticated((ClaimNames.Subject, Subject), (ClaimNames.UserName, "maker.alpha"));

        AuditActor.FromPrincipal(principal).DisplayName.ShouldBe("maker.alpha");
    }

    [Fact]
    public void User_with_a_blank_name_is_recorded_with_the_user_name()
    {
        var principal = Authenticated((ClaimNames.Subject, Subject), (ClaimNames.Name, "  "), (ClaimNames.UserName, "maker.alpha"));

        AuditActor.FromPrincipal(principal).DisplayName.ShouldBe("maker.alpha");
    }

    [Fact]
    public void User_without_any_name_has_no_display_name()
    {
        AuditActor.FromPrincipal(Authenticated((ClaimNames.Subject, Subject))).DisplayName.ShouldBeNull();
    }

    [Fact]
    public void Regulator_staff_have_no_institution()
    {
        AuditActor.FromPrincipal(Authenticated((ClaimNames.Subject, Subject))).InstitutionCode.ShouldBeNull();
    }

    [Fact]
    public void Blank_institution_is_treated_as_none()
    {
        var principal = Authenticated((ClaimNames.Subject, Subject), (ClaimNames.InstitutionId, " "));

        AuditActor.FromPrincipal(principal).InstitutionCode.ShouldBeNull();
    }

    [Fact]
    public void Authenticated_principal_without_a_subject_is_anonymous()
    {
        AuditActor.FromPrincipal(Authenticated((ClaimNames.Name, "Nadia Fernhill"))).ShouldBe(AuditActor.Anonymous);
    }

    [Fact]
    public void Authenticated_principal_with_a_blank_subject_is_anonymous()
    {
        AuditActor.FromPrincipal(Authenticated((ClaimNames.Subject, " "))).ShouldBe(AuditActor.Anonymous);
    }

    [Fact]
    public void Client_credentials_token_is_recorded_as_an_api_client()
    {
        var principal = Authenticated(
            (ClaimNames.AuthorizedUserType, ClaimNames.ApplicationTokenType),
            (ClaimNames.ClientId, ClientId),
            (ClaimNames.AuthorizedParty, "other-azp"),
            (ClaimNames.Subject, "other-sub"),
            (ClaimNames.Name, "ignored"),
            (ClaimNames.InstitutionId, "ALPHA"));

        AuditActor.FromPrincipal(principal).ShouldBe(new AuditActor(ActorType.ApiClient, ClientId, null, "ALPHA"));
    }

    [Fact]
    public void Api_client_falls_back_to_the_authorized_party()
    {
        var principal = Authenticated(
            (ClaimNames.AuthorizedUserType, ClaimNames.ApplicationTokenType),
            (ClaimNames.ClientId, " "),
            (ClaimNames.AuthorizedParty, ClientId),
            (ClaimNames.Subject, "other-sub"));

        AuditActor.FromPrincipal(principal).SubjectId.ShouldBe(ClientId);
    }

    [Fact]
    public void Api_client_falls_back_to_the_subject()
    {
        var principal = Authenticated((ClaimNames.AuthorizedUserType, ClaimNames.ApplicationTokenType), (ClaimNames.Subject, ClientId));

        AuditActor.FromPrincipal(principal).ShouldBe(new AuditActor(ActorType.ApiClient, ClientId, null, null));
    }

    [Fact]
    public void Api_client_without_any_identifier_is_anonymous()
    {
        var principal = Authenticated((ClaimNames.AuthorizedUserType, ClaimNames.ApplicationTokenType), (ClaimNames.InstitutionId, "ALPHA"));

        AuditActor.FromPrincipal(principal).ShouldBe(AuditActor.Anonymous);
    }

    [Fact]
    public void Token_issued_to_a_signed_in_user_is_recorded_as_the_user()
    {
        var principal = Authenticated(
            (ClaimNames.AuthorizedUserType, "APPLICATION_USER"),
            (ClaimNames.Subject, Subject),
            (ClaimNames.ClientId, ClientId));

        AuditActor.FromPrincipal(principal).ShouldBe(new AuditActor(ActorType.User, Subject, null, null));
    }

    [Fact]
    public void Record_carries_the_actor_and_the_event_details()
    {
        var actor = new AuditActor(ActorType.User, Subject, "Nadia Fernhill", "ALPHA");

        var record = actor.ToRecord(AuditAction.AccessDenied, "path=/x", "10.1.2.3", "trace-1", "Submission", "42");

        record.ShouldBe(new AuditRecord(
            AuditAction.AccessDenied, ActorType.User, Subject, "Nadia Fernhill", "ALPHA", "Submission", "42", "path=/x", "10.1.2.3", "trace-1"));
    }

    private static ClaimsPrincipal Authenticated(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), authenticationType: "Test"));
}
