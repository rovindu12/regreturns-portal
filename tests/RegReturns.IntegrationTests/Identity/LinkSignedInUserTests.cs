using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Application.Identity;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;
using RegReturns.Infrastructure.Persistence;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Identity;

public sealed class LinkSignedInUserTests(SqlServerFixture sql)
{
    private const string Sub = "6f1c2a3b-4d5e-4f60-8a7b-9c0d1e2f3a4b";
    private const string NewSub = "0a9b8c7d-6e5f-4a3b-9c2d-1e0f9a8b7c6d";

    [Fact]
    public async Task First_sign_in_creates_a_linked_user_from_the_claims()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, alphaId) = await CreateDatabaseAsync(ct);

        var result = await HandleAsync(connection, Maker(Sub, "Maker.Alpha", "Nadia Fernhill"), ct);

        result.IsSuccess.ShouldBeTrue();
        var user = await SingleUserAsync(connection, ct);
        result.Value.ShouldBe(new SignedInUserLink(user.Id, "Nadia Fernhill", "ALPHA"));
        user.Wso2UserId.ShouldBe(Sub);
        user.UserName.ShouldBe("maker.alpha");
        user.Email.ShouldBe("maker.alpha@alpha.example");
        user.InstitutionId.ShouldBe(alphaId);
        user.Roles.ShouldBe([Role.BankMaker]);
        user.Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public async Task Regulator_staff_are_created_without_an_institution()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, _) = await CreateDatabaseAsync(ct);
        var command = new LinkSignedInUser(Sub, "approver", "Ravi Okonkwo", "approver@valoria.example", null, [Role.SupervisorApprover]);

        (await HandleAsync(connection, command, ct)).IsSuccess.ShouldBeTrue();

        (await SingleUserAsync(connection, ct)).InstitutionId.ShouldBeNull();
    }

    [Fact]
    public async Task Second_sign_in_syncs_name_e_mail_and_roles_from_wso2()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, _) = await CreateDatabaseAsync(ct);
        await HandleAsync(connection, Maker(Sub, "maker.alpha", "Nadia Fernhill"), ct);
        var promoted = Maker(Sub, "maker.alpha", "Nadia Fernhill-Grey") with
        {
            Email = "Nadia.Grey@Alpha.example",
            Roles = [Role.BankMaker, Role.BankChecker],
        };

        var result = await HandleAsync(connection, promoted, ct);

        result.Value.DisplayName.ShouldBe("Nadia Fernhill-Grey");
        var user = await SingleUserAsync(connection, ct);
        user.DisplayName.ShouldBe("Nadia Fernhill-Grey");
        user.Email.ShouldBe("nadia.grey@alpha.example");
        user.Roles.ShouldBe([Role.BankMaker, Role.BankChecker]);
    }

    [Fact]
    public async Task Demo_account_with_a_new_subject_is_relinked_to_the_existing_user()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, alphaId) = await CreateDatabaseAsync(ct);
        var demoUserId = await AddUserAsync(connection, alphaId, isDemoAccount: true, linkedTo: Sub, ct);

        var result = await HandleAsync(connection, Maker(NewSub, "maker.alpha", "Nadia Fernhill"), ct);

        result.Value.AppUserId.ShouldBe(demoUserId);
        (await SingleUserAsync(connection, ct)).Wso2UserId.ShouldBe(NewSub);
    }

    [Fact]
    public async Task Real_account_linked_to_another_subject_is_refused_and_keeps_its_link()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, _) = await CreateDatabaseAsync(ct);
        await HandleAsync(connection, Maker(Sub, "maker.alpha", "Nadia Fernhill"), ct);

        var result = await HandleAsync(connection, Maker(NewSub, "maker.alpha", "Someone Else"), ct);

        result.Error.ShouldBe(LinkSignedInUserHandler.IdentityConflict);
        var user = await SingleUserAsync(connection, ct);
        user.Wso2UserId.ShouldBe(Sub);
        user.DisplayName.ShouldBe("Nadia Fernhill");
    }

    [Fact]
    public async Task Token_naming_another_institution_is_refused_and_the_user_keeps_theirs()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, alphaId) = await CreateDatabaseAsync(ct);
        await AddInstitutionAsync(connection, "BETA", active: true, ct);
        await HandleAsync(connection, Maker(Sub, "maker.alpha", "Nadia Fernhill"), ct);

        var result = await HandleAsync(connection, Maker(Sub, "maker.alpha", "Nadia Fernhill") with { InstitutionCode = "BETA" }, ct);

        result.Error.ShouldBe(LinkSignedInUserHandler.InstitutionChanged);
        (await SingleUserAsync(connection, ct)).InstitutionId.ShouldBe(alphaId);
    }

    [Fact]
    public async Task Bank_user_whose_token_drops_the_institution_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, _) = await CreateDatabaseAsync(ct);
        await HandleAsync(connection, Maker(Sub, "maker.alpha", "Nadia Fernhill"), ct);
        var regulator = Maker(Sub, "maker.alpha", "Nadia Fernhill") with { InstitutionCode = null, Roles = [Role.SupervisorReviewer] };

        var result = await HandleAsync(connection, regulator, ct);

        result.Error.ShouldBe(LinkSignedInUserHandler.InstitutionChanged);
        (await SingleUserAsync(connection, ct)).Roles.ShouldBe([Role.BankMaker]);
    }

    [Fact]
    public async Task Disabled_user_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, alphaId) = await CreateDatabaseAsync(ct);
        var userId = await AddUserAsync(connection, alphaId, isDemoAccount: false, linkedTo: Sub, ct);
        await using (var db = SqlServerFixture.CreateContext(connection))
        {
            (await db.Users.SingleAsync(u => u.Id == userId, ct)).Disable().IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(ct);
        }

        var result = await HandleAsync(connection, Maker(Sub, "maker.alpha", "Nadia Fernhill"), ct);

        result.Error.ShouldBe(LinkSignedInUserHandler.UserDisabled);
    }

    [Fact]
    public async Task Inactive_institution_is_refused_and_nothing_is_stored()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, _) = await CreateDatabaseAsync(ct);
        await AddInstitutionAsync(connection, "GAMMA", active: false, ct);

        var result = await HandleAsync(connection, Maker(Sub, "maker.gamma", "Gamma Maker") with { InstitutionCode = "GAMMA" }, ct);

        result.Error.ShouldBe(LinkSignedInUserHandler.InactiveInstitution);
        (await UserCountAsync(connection, ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Institution_code_in_another_case_links_and_returns_the_stored_code()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, alphaId) = await CreateDatabaseAsync(ct);

        var result = await HandleAsync(connection, Maker(Sub, "maker.alpha", "Nadia Fernhill") with { InstitutionCode = "alpha" }, ct);

        result.Value.InstitutionCode.ShouldBe("ALPHA");
        (await SingleUserAsync(connection, ct)).InstitutionId.ShouldBe(alphaId);
    }

    [Fact]
    public async Task Provisioned_user_is_linked_on_first_sign_in_whatever_the_user_name_case()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, alphaId) = await CreateDatabaseAsync(ct);
        await using (var db = SqlServerFixture.CreateContext(connection))
        {
            var provisioned = AppUser.Create("maker.alpha", "Nadia Fernhill", "maker.alpha@alpha.example", alphaId, [Role.BankMaker]).Value;
            await db.Users.AddAsync(provisioned, ct);
            await db.SaveChangesAsync(ct);
        }

        var result = await HandleAsync(connection, Maker(Sub, "  MAKER.Alpha ", "Nadia Fernhill"), ct);

        var user = await SingleUserAsync(connection, ct);
        result.Value.AppUserId.ShouldBe(user.Id);
        user.Wso2UserId.ShouldBe(Sub);
    }

    [Fact]
    public async Task Missing_display_name_falls_back_to_the_user_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, _) = await CreateDatabaseAsync(ct);

        var result = await HandleAsync(connection, Maker(Sub, "Maker.Alpha", displayName: "  "), ct);

        result.Value.DisplayName.ShouldBe("maker.alpha");
    }

    [Fact]
    public async Task Unknown_institution_is_refused_and_nothing_is_stored()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, _) = await CreateDatabaseAsync(ct);

        var result = await HandleAsync(connection, Maker(Sub, "maker.ghost", "Ghost") with { InstitutionCode = "GHOST" }, ct);

        result.Error.ShouldBe(LinkSignedInUserHandler.UnknownInstitution);
        result.Error!.Code.ShouldBe("User.UnknownInstitution");
        (await UserCountAsync(connection, ct)).ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_e_mail_is_refused_and_nothing_is_stored(string? email)
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, _) = await CreateDatabaseAsync(ct);

        var result = await HandleAsync(connection, Maker(Sub, "maker.alpha", "Nadia Fernhill") with { Email = email }, ct);

        result.Error.ShouldBe(LinkSignedInUserHandler.EmailMissing);
        result.Error!.Code.ShouldBe("User.EmailMissing");
        (await UserCountAsync(connection, ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Bank_role_without_an_institution_is_refused_by_the_role_rules()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, _) = await CreateDatabaseAsync(ct);

        var result = await HandleAsync(connection, Maker(Sub, "maker.alpha", "Nadia Fernhill") with { InstitutionCode = null }, ct);

        result.Error.ShouldBe(IdentityErrors.InstitutionRequired);
        (await UserCountAsync(connection, ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Sync_that_breaks_the_role_rules_leaves_the_user_unchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var (connection, _) = await CreateDatabaseAsync(ct);
        await HandleAsync(connection, Maker(Sub, "maker.alpha", "Nadia Fernhill"), ct);

        var result = await HandleAsync(connection, Maker(Sub, "maker.alpha", "Renamed") with { Roles = [] }, ct);

        result.Error.ShouldBe(IdentityErrors.RoleRequired);
        var user = await SingleUserAsync(connection, ct);
        user.DisplayName.ShouldBe("Nadia Fernhill");
        user.Roles.ShouldBe([Role.BankMaker]);
    }

    private static LinkSignedInUser Maker(string sub, string userName, string? displayName) =>
        new(sub, userName, displayName, $"{userName.Trim()}@alpha.example", "ALPHA", [Role.BankMaker]);

    private static async Task<Result<SignedInUserLink>> HandleAsync(string connection, LinkSignedInUser command, CancellationToken ct)
    {
        // A fresh context per call, as each sign-in is its own request.
        await using var db = SqlServerFixture.CreateContext(connection);
        return await new LinkSignedInUserHandler(db).HandleAsync(command, ct);
    }

    private static async Task<Guid> AddUserAsync(string connection, Guid institutionId, bool isDemoAccount, string linkedTo, CancellationToken ct)
    {
        await using var db = SqlServerFixture.CreateContext(connection);
        var user = AppUser.Create("maker.alpha", "Nadia Fernhill", "maker.alpha@alpha.example", institutionId, [Role.BankMaker], isDemoAccount).Value;
        user.LinkIdentity(linkedTo);
        await db.Users.AddAsync(user, ct);
        await db.SaveChangesAsync(ct);
        return user.Id;
    }

    private static async Task AddInstitutionAsync(string connection, string code, bool active, CancellationToken ct)
    {
        await using var db = SqlServerFixture.CreateContext(connection);
        var institution = Institution.Create(code, $"{code} Bank of Valoria PLC", LicenceCategory.Commercial);
        if (!active)
        {
            institution.Deactivate();
        }

        await db.Institutions.AddAsync(institution, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<AppUser> SingleUserAsync(string connection, CancellationToken ct)
    {
        await using var db = SqlServerFixture.CreateContext(connection);
        return await db.Users.AsNoTracking().SingleAsync(ct);
    }

    private static async Task<int> UserCountAsync(string connection, CancellationToken ct)
    {
        await using var db = SqlServerFixture.CreateContext(connection);
        return await db.Users.CountAsync(ct);
    }

    private async Task<(string Connection, Guid AlphaId)> CreateDatabaseAsync(CancellationToken ct)
    {
        var connection = sql.NewDatabaseConnectionString();
        await using var db = SqlServerFixture.CreateContext(connection);
        await new DatabaseInitializer(db, new FakeTimeProvider(SqlServerFixture.SeedDate), NullLogger<DatabaseInitializer>.Instance)
            .MigrateAsync(ct);
        var alpha = Institution.Create("ALPHA", "Alpha Bank of Valoria PLC", LicenceCategory.Commercial);
        await db.Institutions.AddAsync(alpha, ct);
        await db.SaveChangesAsync(ct);
        return (connection, alpha.Id);
    }
}
