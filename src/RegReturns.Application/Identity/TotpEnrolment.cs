using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Auditing;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;

namespace RegReturns.Application.Identity;

/// <summary>An account in WSO2, the identity directory (ADR 0032).</summary>
/// <param name="Id">WSO2's user id (the SCIM id, also the token's subject).</param>
/// <param name="UserName">The user name.</param>
/// <param name="PortalRoles">The portal roles the account holds (<see cref="RoleNames"/>), in no particular order.</param>
public sealed record IdentityAccount(string Id, string UserName, IReadOnlyList<string> PortalRoles);

/// <summary>
/// WSO2 Identity Server as the directory of accounts, over SCIM 2 with the provisioner client (ADR 0032). Failures to
/// reach or use WSO2 are thrown as <see cref="IdentityDirectoryException"/>.
/// </summary>
public interface IIdentityDirectory
{
    /// <summary>Finds an account by its exact user name.</summary>
    /// <param name="userName">The user name.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The account, or <see langword="null"/> if WSO2 has none by that name.</returns>
    Task<IdentityAccount?> FindByUserNameAsync(string userName, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a TOTP enrolment window: until <paramref name="until"/>, the account's next sign-in to the portal replaces
    /// its authenticator (<see cref="TotpEnrolmentClaim"/>).
    /// </summary>
    /// <param name="accountId">WSO2's user id.</param>
    /// <param name="until">When the window closes.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes once WSO2 has stored the window.</returns>
    Task OpenTotpEnrolmentAsync(string accountId, DateTimeOffset until, CancellationToken cancellationToken);
}

/// <summary>WSO2 could not be reached, refused the provisioner, or answered something unexpected.</summary>
public sealed class IdentityDirectoryException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="IdentityDirectoryException"/> class.</summary>
    public IdentityDirectoryException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IdentityDirectoryException"/> class.</summary>
    /// <param name="message">What failed, without secrets.</param>
    public IdentityDirectoryException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IdentityDirectoryException"/> class.</summary>
    /// <param name="message">What failed, without secrets.</param>
    /// <param name="innerException">The cause.</param>
    public IdentityDirectoryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Settings of TOTP enrolment by administrators (section <c>Iam:TotpEnrolment</c>, ADR 0032).</summary>
public sealed class TotpEnrolmentOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Iam:TotpEnrolment";

    /// <summary>Gets or sets how long the person has to sign in and set up their authenticator.</summary>
    [Range(1, 168)]
    public int WindowHours { get; set; } = 72;
}

/// <summary>Why opening a TOTP enrolment was refused.</summary>
public static class TotpEnrolmentErrors
{
    /// <summary>The user name is not a valid WSO2 user name.</summary>
    public static readonly Error InvalidUserName = new(
        "User.InvalidUserName", "Enter a user name: lower-case letters, digits, dots, hyphens and underscores.");

    /// <summary>An administrator cannot reset their own authenticator (another administrator must).</summary>
    public static readonly Error CannotResetOwn = new(
        "User.CannotResetOwnAuthenticator", "You cannot reset your own authenticator. Ask another administrator.");

    /// <summary>WSO2 has no account by that name.</summary>
    public static readonly Error NotInDirectory = new("User.NotInDirectory", "WSO2 has no account with this user name.");

    /// <summary>The account holds no portal role, so it is not the portal's to change (WSO2's own administrators).</summary>
    public static readonly Error NoPortalRole = new("User.NoPortalRole", "This account holds no portal role.");

    /// <summary>The person's portal access is disabled.</summary>
    public static readonly Error UserDisabled = new(
        "User.Disabled", "This person's portal access is disabled. Re-enable it before resetting their authenticator.");

    /// <summary>WSO2 could not be reached or refused the change.</summary>
    public static readonly Error DirectoryUnavailable = new(
        "Identity.DirectoryUnavailable", "WSO2 could not be reached or refused the change. Nothing was changed; try again.");
}

/// <summary>
/// Lets a person set up a new authenticator at their next sign-in: for someone who has never signed in and must use
/// two-step verification, or who has lost their phone (ADR 0032).
/// </summary>
/// <param name="UserName">The person's WSO2 user name.</param>
public sealed record OpenTotpEnrolment(string UserName);

/// <summary>An open enrolment window.</summary>
/// <param name="UserName">The person's user name.</param>
/// <param name="Until">When the window closes.</param>
/// <param name="AuditSequence">The audit entry that records it.</param>
public sealed record TotpEnrolmentWindow(string UserName, DateTimeOffset Until, long AuditSequence);

/// <summary>
/// Opens a TOTP enrolment window for a system administrator. Refused for demo, system and API client accounts, for
/// accounts that hold no portal role, for disabled people and for the caller's own account. The window is set in WSO2
/// (the portal's sign-in script reads it) and recorded as a <see cref="AuditAction.TotpEnrolmentOpened"/> event with
/// the user name and the end of the window. The old authenticator keeps working until the person enrols the new one.
/// </summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
/// <param name="directory">WSO2.</param>
/// <param name="auditTrail">Records the change.</param>
/// <param name="auditContext">Who acts, from where.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="options">The window length.</param>
/// <param name="logger">The logger.</param>
public sealed partial class OpenTotpEnrolmentHandler(
    IAppDbContext db,
    ICurrentActor currentActor,
    IIdentityDirectory directory,
    IAuditTrail auditTrail,
    IAuditContext auditContext,
    TimeProvider timeProvider,
    IOptions<TotpEnrolmentOptions> options,
    ILogger<OpenTotpEnrolmentHandler> logger) : ICommandHandler<OpenTotpEnrolment, Result<TotpEnrolmentWindow>>
{
    /// <summary>The audit entity type of the event: a WSO2 account, identified by its user name.</summary>
    public const string AuditEntityType = "Account";

    /// <inheritdoc />
    public async Task<Result<TotpEnrolmentWindow>> HandleAsync(OpenTotpEnrolment command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var actor = await currentActor.GetAsync(cancellationToken);
        if (actor.IsFailure || !actor.Value.HasRole(Role.SystemAdmin))
        {
            return Refuse(UserAdministrationErrors.AdminsOnly);
        }

        var userName = command.UserName?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!UserNamePattern().IsMatch(userName))
        {
            return Refuse(TotpEnrolmentErrors.InvalidUserName);
        }

        if (userName == AppUser.MigrationUserName || userName.StartsWith(AppUser.ApiClientUserNamePrefix, StringComparison.Ordinal))
        {
            return Refuse(IdentityErrors.SystemAccountProtected);
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UserName == userName, cancellationToken);
        var refusal = user switch
        {
            null => null,
            { Id: var id } when id == actor.Value.UserId => TotpEnrolmentErrors.CannotResetOwn,
            { IsDemoAccount: true } => IdentityErrors.DemoAccountProtected,
            { ApiClientId: not null } => IdentityErrors.SystemAccountProtected,
            { Status: UserStatus.Disabled } => TotpEnrolmentErrors.UserDisabled,
            _ => null,
        };
        if (refusal is not null)
        {
            return Refuse(refusal);
        }

        var until = timeProvider.GetUtcNow().AddHours(options.Value.WindowHours);
        IdentityAccount? account;
        try
        {
            account = await directory.FindByUserNameAsync(userName, cancellationToken);
            if (account is null)
            {
                return Refuse(TotpEnrolmentErrors.NotInDirectory);
            }

            // Linked people sign in as their WSO2 id; a different id under the same name is not the same person.
            if (user?.Wso2UserId is { } linked && linked != account.Id)
            {
                return Refuse(LinkSignedInUserHandler.IdentityConflict);
            }

            if (!account.PortalRoles.Any(role => RoleNames.All.Contains(role)))
            {
                return Refuse(TotpEnrolmentErrors.NoPortalRole);
            }

            await directory.OpenTotpEnrolmentAsync(account.Id, until, cancellationToken);
        }
        catch (IdentityDirectoryException ex)
        {
            LogDirectoryFailed(logger, ex);
            return TotpEnrolmentErrors.DirectoryUnavailable;
        }

        var details = JsonSerializer.Serialize(new { userName, until = until.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) });
        var sequence = await auditTrail.RecordAsync(
            auditContext.Current.ToRecord(AuditAction.TotpEnrolmentOpened, details, AuditEntityType, account.Id), cancellationToken);
        LogOpened(logger, account.Id, until, sequence);
        return new TotpEnrolmentWindow(userName, until, sequence);
    }

    private Error Refuse(Error error)
    {
        LogRefused(logger, error.Code);
        return error;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UserNamePattern();

    [LoggerMessage(EventId = 3013, Level = LogLevel.Information,
        Message = "TOTP enrolment opened for WSO2 user {AccountId} until {Until} (audit entry {AuditSequence})")]
    private static partial void LogOpened(ILogger logger, string accountId, DateTimeOffset until, long auditSequence);

    [LoggerMessage(EventId = 3014, Level = LogLevel.Warning, Message = "TOTP enrolment refused: {ErrorCode}")]
    private static partial void LogRefused(ILogger logger, string errorCode);

    [LoggerMessage(EventId = 3015, Level = LogLevel.Error, Message = "TOTP enrolment failed: WSO2 could not be used")]
    private static partial void LogDirectoryFailed(ILogger logger, Exception exception);
}

/// <summary>The directory of a host that does not manage accounts: every call fails as if WSO2 were unreachable.</summary>
public sealed class UnavailableIdentityDirectory : IIdentityDirectory
{
    /// <inheritdoc />
    public Task<IdentityAccount?> FindByUserNameAsync(string userName, CancellationToken cancellationToken) =>
        Task.FromException<IdentityAccount?>(new IdentityDirectoryException(Unavailable));

    /// <inheritdoc />
    public Task OpenTotpEnrolmentAsync(string accountId, DateTimeOffset until, CancellationToken cancellationToken) =>
        Task.FromException(new IdentityDirectoryException(Unavailable));

    private const string Unavailable = "This host does not manage WSO2 accounts.";
}
