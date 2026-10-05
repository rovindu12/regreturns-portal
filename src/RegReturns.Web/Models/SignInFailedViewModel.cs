using System.Collections.Frozen;

using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;
using RegReturns.Web.Identity;

namespace RegReturns.Web.Models;

/// <summary>Data for the sign-in failure page.</summary>
/// <param name="Message">What went wrong, in plain words.</param>
/// <param name="ErrorReference">The trace id of the failed request, if it was a valid one.</param>
/// <param name="ContactAdministrator">Whether the user's account needs fixing rather than another attempt.</param>
public sealed record SignInFailedViewModel(string Message, string? ErrorReference, bool ContactAdministrator)
{
    /// <summary>Shown for reasons without a specific message.</summary>
    public const string GenericMessage = "We could not sign you in. Please try again.";

    private const string Unavailable = "The sign-in service is not available right now. Please try again in a few minutes.";
    private const string RolesMismatch = "Your account's roles and institution do not match.";
    private const string NotIdentified = "The sign-in service did not identify your account.";

    private static readonly FrozenDictionary<string, (string Message, bool ContactAdministrator)> Reasons =
        new Dictionary<string, (string, bool)>(StringComparer.Ordinal)
        {
            [SignInFailureCodes.AccessDenied] = ("Sign-in was cancelled or refused by the sign-in service.", false),
            [SignInFailureCodes.RequestExpired] = ("The sign-in request expired or was started in another tab. Please sign in again.", false),
            [SignInFailureCodes.IdentityProviderUnreachable] = (Unavailable, false),
            [SignInFailureCodes.ServerError] = (Unavailable, false),
            [SignInFailureCodes.TemporarilyUnavailable] = (Unavailable, false),
            [LinkSignedInUserHandler.UnknownInstitution.Code] = ("Your account belongs to an institution the portal does not know.", true),
            [LinkSignedInUserHandler.EmailMissing.Code] = ("Your account has no e-mail address, which the portal requires.", true),
            [IdentityErrors.RoleRequired.Code] = ("Your account has no portal role yet.", true),
            [IdentityErrors.MixedRoles.Code] = (RolesMismatch, true),
            [IdentityErrors.InstitutionRequired.Code] = (RolesMismatch, true),
            [IdentityErrors.InstitutionNotAllowed.Code] = (RolesMismatch, true),
            [SignInErrors.SubjectMissing.Code] = (NotIdentified, true),
            [SignInErrors.UserNameMissing.Code] = (NotIdentified, true),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Builds the page from the (untrusted) query string values.</summary>
    /// <param name="reason">The failure code.</param>
    /// <param name="reference">The trace id of the failed request.</param>
    /// <returns>The view model.</returns>
    public static SignInFailedViewModel Create(string? reason, string? reference)
    {
        var (message, contactAdministrator) = reason is not null && Reasons.TryGetValue(reason, out var known)
            ? known
            : (GenericMessage, false);
        return new SignInFailedViewModel(message, IsTraceId(reference) ? reference : null, contactAdministrator);
    }

    private static bool IsTraceId(string? value) =>
        value is { Length: 32 } && value.All(char.IsAsciiHexDigitLower);
}
