using RegReturns.Domain.Identity;

namespace RegReturns.UnitTests.Domain;

public sealed class AppUserTests
{
    private static readonly Guid BankId = Guid.CreateVersion7();

    [Fact]
    public void Bank_user_with_institution_is_created_with_normalised_names()
    {
        var result = AppUser.Create("  Maker.HLB ", "Maker", "Maker@HLB.example", BankId, [Role.BankMaker]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserName.ShouldBe("maker.hlb");
        result.Value.Email.ShouldBe("maker@hlb.example");
        result.Value.Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public void Bank_role_without_institution_is_rejected()
    {
        AppUser.Create("maker", "Maker", "m@x.example", null, [Role.BankMaker]).Error
            .ShouldBe(IdentityErrors.InstitutionRequired);
    }

    [Fact]
    public void Regulator_role_with_institution_is_rejected()
    {
        AppUser.Create("reviewer", "Reviewer", "r@x.example", BankId, [Role.SupervisorReviewer]).Error
            .ShouldBe(IdentityErrors.InstitutionNotAllowed);
    }

    [Fact]
    public void Bank_and_regulator_roles_cannot_be_mixed()
    {
        AppUser.Create("mixed", "Mixed", "x@x.example", BankId, [Role.BankChecker, Role.SupervisorApprover]).Error
            .ShouldBe(IdentityErrors.MixedRoles);
    }

    [Fact]
    public void At_least_one_role_is_required()
    {
        AppUser.Create("none", "None", "n@x.example", null, []).Error.ShouldBe(IdentityErrors.RoleRequired);
    }

    [Fact]
    public void Demo_accounts_cannot_be_disabled()
    {
        var user = AppUser.Create("auditor", "Auditor", "a@x.example", null, [Role.Auditor], isDemoAccount: true).Value;

        user.Disable().Error.ShouldBe(IdentityErrors.DemoAccountProtected);
        user.Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public void Actor_carries_roles_and_institution()
    {
        var user = AppUser.Create("checker", "Checker", "c@x.example", BankId, [Role.BankChecker]).Value;

        var actor = user.ToActor();

        actor.UserId.ShouldBe(user.Id);
        actor.HasRole(Role.BankChecker).ShouldBeTrue();
        actor.BelongsTo(BankId).ShouldBeTrue();
        actor.IsRegulatorStaff.ShouldBeFalse();
    }
}
