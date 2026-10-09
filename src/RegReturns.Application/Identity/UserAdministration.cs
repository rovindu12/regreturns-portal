using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;

namespace RegReturns.Application.Identity;

/// <summary>Why a directory request was refused.</summary>
public static class UserAdministrationErrors
{
    /// <summary>Only a system administrator manages users.</summary>
    public static readonly Error AdminsOnly = new("User.AdminsOnly", "Only a system administrator can manage users.");

    /// <summary>No such user.</summary>
    public static readonly Error NotFound = new("User.NotFound", "The user does not exist.");

    /// <summary>An administrator cannot lock themselves out.</summary>
    public static readonly Error CannotChangeOwnAccess = new("User.CannotChangeOwnAccess", "You cannot change your own access.");
}

/// <summary>Asks for the user directory: people and API clients (ADR 0031).</summary>
[SuppressMessage(
    "Major Code Smell",
    "S2094:Classes should not be empty",
    Justification = "A query without inputs: the handler interface needs a type to dispatch on.")]
public sealed record GetUserDirectory;

/// <summary>A person in the directory.</summary>
/// <param name="Id">The user id.</param>
/// <param name="UserName">The WSO2 user name.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Roles">The roles, in role order.</param>
/// <param name="InstitutionCode">The bank's code, for bank staff.</param>
/// <param name="Status">Whether the user may sign in.</param>
/// <param name="IsDemoAccount">Whether it is a protected demo account.</param>
/// <param name="IsLinked">Whether the user has signed in through WSO2 (linked by subject id).</param>
/// <param name="CanChangeAccess">Whether the caller may disable or re-enable the user.</param>
public sealed record DirectoryUser(
    Guid Id,
    string UserName,
    string DisplayName,
    IReadOnlyList<Role> Roles,
    string? InstitutionCode,
    UserStatus Status,
    bool IsDemoAccount,
    bool IsLinked,
    bool CanChangeAccess);

/// <summary>A registered API client.</summary>
/// <param name="ClientId">The WSO2 client id.</param>
/// <param name="Name">The application name.</param>
/// <param name="InstitutionCode">The bank it acts for.</param>
/// <param name="IsActive">Whether it may call the API.</param>
public sealed record DirectoryApiClient(string ClientId, string Name, string InstitutionCode, bool IsActive);

/// <summary>The user directory.</summary>
/// <param name="People">People, regulator staff first and then by bank and user name.</param>
/// <param name="ApiClients">API clients by bank and client id.</param>
public sealed record UserDirectory(IReadOnlyList<DirectoryUser> People, IReadOnlyList<DirectoryApiClient> ApiClients);

/// <summary>Lists the directory for a system administrator. The system account and client users are not people and are not listed.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
public sealed class GetUserDirectoryHandler(IAppDbContext db, ICurrentActor currentActor)
    : IQueryHandler<GetUserDirectory, Result<UserDirectory>>
{
    /// <inheritdoc />
    public async Task<Result<UserDirectory>> HandleAsync(GetUserDirectory query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var actor = await currentActor.GetAsync(cancellationToken);
        if (actor.IsFailure || !actor.Value.HasRole(Role.SystemAdmin))
        {
            return UserAdministrationErrors.AdminsOnly;
        }

        var banks = await db.Institutions.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Code, cancellationToken);
        var users = await db.Users.AsNoTracking()
            .Where(u => u.ApiClientId == null && u.UserName != AppUser.MigrationUserName)
            .ToListAsync(cancellationToken);
        var clients = await db.ApiClients.AsNoTracking().ToListAsync(cancellationToken);

        string? BankOf(Guid? id) => id is { } bankId ? banks.GetValueOrDefault(bankId) : null;
        var people = users
            .Select(u => new DirectoryUser(
                u.Id,
                u.UserName,
                u.DisplayName,
                [.. u.Roles.Order()],
                BankOf(u.InstitutionId),
                u.Status,
                u.IsDemoAccount,
                u.Wso2UserId is not null,
                !u.IsDemoAccount && u.Id != actor.Value.UserId))
            .OrderBy(u => u.InstitutionCode is not null)
            .ThenBy(u => u.InstitutionCode, StringComparer.Ordinal)
            .ThenBy(u => u.UserName, StringComparer.Ordinal)
            .ToList();
        var apiClients = clients
            .Select(c => new DirectoryApiClient(c.Wso2ClientId, c.Name, BankOf(c.InstitutionId) ?? string.Empty, c.IsActive))
            .OrderBy(c => c.InstitutionCode, StringComparer.Ordinal)
            .ThenBy(c => c.ClientId, StringComparer.Ordinal)
            .ToList();
        return new UserDirectory(people, apiClients);
    }
}

/// <summary>Disables or re-enables a person's portal access.</summary>
/// <param name="UserId">The user id.</param>
/// <param name="Enable"><see langword="true"/> to re-enable, <see langword="false"/> to disable.</param>
public sealed record ChangeUserAccess(Guid UserId, bool Enable);

/// <summary>
/// Disables or re-enables a person's portal access for a system administrator. Demo accounts, client and system
/// accounts and the caller's own account are refused. A disabled user's next request no longer resolves to an actor,
/// and signing in is refused. WSO2 keeps the account; the save is audited as a state change.
/// </summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
/// <param name="logger">The logger.</param>
public sealed partial class ChangeUserAccessHandler(IAppDbContext db, ICurrentActor currentActor, ILogger<ChangeUserAccessHandler> logger)
    : ICommandHandler<ChangeUserAccess, Result<UserStatus>>
{
    /// <inheritdoc />
    public async Task<Result<UserStatus>> HandleAsync(ChangeUserAccess command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var actor = await currentActor.GetAsync(cancellationToken);
        if (actor.IsFailure || !actor.Value.HasRole(Role.SystemAdmin))
        {
            return Refuse(command, UserAdministrationErrors.AdminsOnly);
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return Refuse(command, UserAdministrationErrors.NotFound);
        }

        if (user.Id == actor.Value.UserId)
        {
            return Refuse(command, UserAdministrationErrors.CannotChangeOwnAccess);
        }

        var changed = command.Enable ? user.Enable() : user.Disable();
        if (changed.IsFailure)
        {
            return Refuse(command, changed.Error!);
        }

        await db.SaveChangesAsync(cancellationToken);
        LogChanged(logger, user.Id, user.Status);
        return user.Status;
    }

    private Error Refuse(ChangeUserAccess command, Error error)
    {
        LogRefused(logger, command.UserId, error.Code);
        return error;
    }

    [LoggerMessage(EventId = 3011, Level = LogLevel.Information, Message = "Portal access of user {UserId} is now {Status}")]
    private static partial void LogChanged(ILogger logger, Guid userId, UserStatus status);

    [LoggerMessage(EventId = 3012, Level = LogLevel.Warning, Message = "Change of portal access for user {UserId} refused: {ErrorCode}")]
    private static partial void LogRefused(ILogger logger, Guid userId, string errorCode);
}
