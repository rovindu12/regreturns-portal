using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;

namespace RegReturns.Infrastructure.Persistence.Seeding;

/// <summary>
/// Demo user directory. The same user names are created in WSO2 by IamBootstrap and linked by user name. Accounts the
/// given <see cref="DemoDirectory"/> already holds are used as they are.
/// </summary>
internal sealed class DemoUsers
{
    public const string Reviewer = "reviewer";
    public const string Approver = "approver";
    public const string ApproverMfa = "approver.mfa";
    public const string Admin = "admin.demo";
    public const string Auditor = "auditor";
    private const string RegulatorDomain = "bov.example";

    private readonly Dictionary<string, AppUser> _byUserName = new(StringComparer.Ordinal);

    private readonly DemoDirectory? _directory;

    public DemoUsers(IReadOnlyDictionary<string, Institution> institutionsByCode, DemoDirectory? directory = null)
    {
        _directory = directory;
        foreach (var bank in DemoBank.All)
        {
            var institution = institutionsByCode[bank.Code];
            Add(MakerUserName(bank.Code), $"Maker ({bank.Name})", $"maker@{bank.Domain}", institution.Id, Role.BankMaker);
            Add(CheckerUserName(bank.Code), $"Checker ({bank.Name})", $"checker@{bank.Domain}", institution.Id, Role.BankChecker);
        }

        Add(Reviewer, "Supervisor Reviewer", $"reviewer@{RegulatorDomain}", null, Role.SupervisorReviewer);
        Add(Approver, "Supervisor Approver", $"approver@{RegulatorDomain}", null, Role.SupervisorApprover);
        Add(ApproverMfa, "Supervisor Approver (MFA)", $"approver.mfa@{RegulatorDomain}", null, Role.SupervisorApprover);
        Add(Admin, "System Administrator", $"admin@{RegulatorDomain}", null, Role.SystemAdmin);
        Add(Auditor, "Auditor", $"auditor@{RegulatorDomain}", null, Role.Auditor);
    }

    public IReadOnlyCollection<AppUser> All => _byUserName.Values;

    public static string MakerUserName(string bankCode) => $"maker.{bankCode.ToLowerInvariant()}";

    public static string CheckerUserName(string bankCode) => $"checker.{bankCode.ToLowerInvariant()}";

    public Actor Maker(string bankCode) => Actor(MakerUserName(bankCode));

    public Actor Checker(string bankCode) => Actor(CheckerUserName(bankCode));

    public Actor Actor(string userName) => _byUserName[userName].ToActor();

    private void Add(string userName, string displayName, string email, Guid? institutionId, Role role)
    {
        if (_directory?.UserNamed(userName) is { } existing)
        {
            _byUserName.Add(userName, existing);
            return;
        }

        var result = AppUser.Create(userName, displayName, email, institutionId, [role], isDemoAccount: true);
        _byUserName.Add(userName, result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error!.Message));
    }
}
