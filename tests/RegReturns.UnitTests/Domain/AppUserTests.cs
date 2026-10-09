using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;

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
    public void Demo_accounts_cannot_be_enabled_either()
    {
        var user = AppUser.Create("auditor", "Auditor", "a@x.example", null, [Role.Auditor], isDemoAccount: true).Value;

        user.Enable().Error.ShouldBe(IdentityErrors.DemoAccountProtected);
    }

    [Fact]
    public void A_person_can_be_disabled_and_enabled_again()
    {
        var user = AppUser.Create("reviewer2", "Reviewer", "r@x.example", null, [Role.SupervisorReviewer]).Value;

        user.Disable().IsSuccess.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Disabled);
        user.Enable().IsSuccess.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public void The_migration_account_cannot_be_disabled()
    {
        var user = AppUser.ForMigration();

        user.Disable().Error.ShouldBe(IdentityErrors.SystemAccountProtected);
        user.Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public void An_api_client_user_cannot_be_disabled_here()
    {
        var bank = Institution.Create("HLB", "Harbourline Bank PLC", LicenceCategory.Commercial);
        var user = AppUser.ForApiClient(ApiClient.Create(bank, "regreturns-bank-hlb", "Harbourline core banking"));

        user.Disable().Error.ShouldBe(IdentityErrors.SystemAccountProtected);
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

    [Fact]
    public void An_api_client_user_is_a_bank_maker_of_the_clients_bank_without_a_sign_in()
    {
        var bank = Institution.Create("HLB", "Harbourline Bank PLC", LicenceCategory.Commercial);
        var client = ApiClient.Create(bank, "regreturns-bank-hlb", "Harbourline core banking");

        var user = AppUser.ForApiClient(client);

        user.UserName.ShouldBe($"{AppUser.ApiClientUserNamePrefix}{client.Id:N}");
        user.DisplayName.ShouldBe("Harbourline core banking");
        user.InstitutionId.ShouldBe(bank.Id);
        user.Roles.ShouldBe([Role.BankMaker]);
        user.Email.ShouldBeNull();
        user.Wso2UserId.ShouldBeNull();
        user.ApiClientId.ShouldBe(client.Id);
        user.IsApiClientUser.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public void The_migration_account_is_regulator_side_with_no_role_e_mail_or_sign_in()
    {
        var account = AppUser.ForMigration();

        account.UserName.ShouldBe(AppUser.MigrationUserName);
        account.UserName.ShouldBe("system.migration");
        account.DisplayName.ShouldBe("Legacy data migration");
        account.Roles.ShouldBeEmpty();
        account.Email.ShouldBeNull();
        account.InstitutionId.ShouldBeNull();
        account.Wso2UserId.ShouldBeNull();
        account.ApiClientId.ShouldBeNull();
        account.IsDemoAccount.ShouldBeFalse();
        account.Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public void The_migration_account_is_a_system_account_and_not_a_client_user()
    {
        var account = AppUser.ForMigration();

        account.IsSystemAccount.ShouldBeTrue();
        account.IsApiClientUser.ShouldBeFalse();
    }

    [Fact]
    public void The_migration_account_acts_as_regulator_staff_without_any_role()
    {
        var account = AppUser.ForMigration();

        var actor = account.ToActor();

        actor.UserId.ShouldBe(account.Id);
        actor.DisplayName.ShouldBe("Legacy data migration");
        actor.IsRegulatorStaff.ShouldBeTrue();
        actor.Roles.ShouldBeEmpty();
    }

    [Fact]
    public void People_and_client_users_are_not_system_accounts()
    {
        var bank = Institution.Create("HLB", "Harbourline Bank PLC", LicenceCategory.Commercial);
        var person = AppUser.Create("approver", "Approver", "a@x.example", null, [Role.SupervisorApprover]).Value;
        var clientUser = AppUser.ForApiClient(ApiClient.Create(bank, "regreturns-bank-hlb", "Harbourline core banking"));

        person.IsSystemAccount.ShouldBeFalse();
        clientUser.IsSystemAccount.ShouldBeFalse();
    }

    [Theory]
    [InlineData("system.migration")]
    [InlineData("  SYSTEM.Migration ")]
    [InlineData("api-client.0199a3b4c5d6")]
    [InlineData("API-Client.anything")]
    public void Reserved_user_names_cannot_be_given_to_a_person(string userName)
    {
        AppUser.Create(userName, "Impostor", "i@x.example", null, [Role.SupervisorApprover]).Error
            .ShouldBe(IdentityErrors.ReservedUserName);
    }

    [Theory]
    [InlineData("system.migration2")]
    [InlineData("migration")]
    [InlineData("api-client")]
    [InlineData("my.api-client.user")]
    public void User_names_that_only_resemble_a_reserved_one_are_allowed(string userName)
    {
        AppUser.Create(userName, "Person", "p@x.example", null, [Role.SupervisorReviewer]).IsSuccess.ShouldBeTrue();
    }
}
