using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Identity;

namespace RegReturns.Application.Demo;

/// <summary>Naming shared by IamBootstrap and the portal for the demo accounts' settings.</summary>
public static class DemoAccounts
{
    /// <summary>
    /// The part of a per-user setting name that identifies the user: the user name in upper case, with every character
    /// other than an ASCII letter or digit replaced by <c>_</c>. IamBootstrap writes <c>TOTP_SECRET_</c> plus this; the
    /// portal reads <c>Demo:TotpSecrets:</c> plus this.
    /// </summary>
    /// <param name="userName">The user name, for example <c>approver.mfa</c>.</param>
    /// <returns>For example <c>APPROVER_MFA</c>.</returns>
    public static string SettingSuffix(string userName)
    {
        ArgumentNullException.ThrowIfNull(userName);
        return new string([.. userName.ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')]);
    }
}

/// <summary>Asks for the demo accounts a visitor can sign in as (ADR 0031).</summary>
[SuppressMessage(
    "Major Code Smell",
    "S2094:Classes should not be empty",
    Justification = "A query without inputs: the handler interface needs a type to dispatch on.")]
public sealed record GetDemoAccounts;

/// <summary>A demo account, as the demo page lists it.</summary>
/// <param name="UserName">The user name to sign in with.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Role">The account's role (demo accounts hold one).</param>
/// <param name="InstitutionCode">The bank's code, for bank staff.</param>
/// <param name="InstitutionName">The bank's name, for bank staff.</param>
public sealed record DemoAccount(string UserName, string DisplayName, Role Role, string? InstitutionCode, string? InstitutionName);

/// <summary>
/// Lists the active demo accounts from the directory, by role and then bank, so the demo page shows exactly the
/// accounts that exist. Empty unless demo mode is on.
/// </summary>
/// <param name="db">The unit of work.</param>
/// <param name="options">The demo settings.</param>
public sealed class GetDemoAccountsHandler(IAppDbContext db, IOptions<DemoOptions> options)
    : IQueryHandler<GetDemoAccounts, IReadOnlyList<DemoAccount>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DemoAccount>> HandleAsync(GetDemoAccounts query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!options.Value.Enabled)
        {
            return [];
        }

        var users = await db.Users.AsNoTracking()
            .Where(u => u.IsDemoAccount && u.Status == UserStatus.Active)
            .ToListAsync(cancellationToken);
        var banks = await db.Institutions.AsNoTracking()
            .Select(i => new { i.Id, i.Code, i.Name })
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        return users
            .Where(u => u.Roles.Count > 0)
            .Select(u =>
            {
                var bank = u.InstitutionId is { } id && banks.TryGetValue(id, out var b) ? b : null;
                return new DemoAccount(u.UserName, u.DisplayName, u.Roles.Min(), bank?.Code, bank?.Name);
            })
            .OrderBy(a => a.Role)
            .ThenBy(a => a.InstitutionCode, StringComparer.Ordinal)
            .ThenBy(a => a.UserName, StringComparer.Ordinal)
            .ToList();
    }
}
