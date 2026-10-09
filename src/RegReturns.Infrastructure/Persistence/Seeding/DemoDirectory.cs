using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;

namespace RegReturns.Infrastructure.Persistence.Seeding;

/// <summary>
/// The institutions and users a demo reset keeps (ADR 0031), so the seed files its returns for them instead of
/// creating new ones.
/// </summary>
/// <param name="Institutions">Existing institutions.</param>
/// <param name="Users">Existing users.</param>
internal sealed record DemoDirectory(IReadOnlyCollection<Institution> Institutions, IReadOnlyCollection<AppUser> Users)
{
    /// <summary>Finds an institution by its code.</summary>
    /// <param name="code">The institution code.</param>
    /// <returns>The institution, or <see langword="null"/>.</returns>
    public Institution? InstitutionWithCode(string code) =>
        Institutions.FirstOrDefault(i => string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a user by user name.</summary>
    /// <param name="userName">The user name.</param>
    /// <returns>The user, or <see langword="null"/>.</returns>
    public AppUser? UserNamed(string userName) =>
        Users.FirstOrDefault(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase));
}
