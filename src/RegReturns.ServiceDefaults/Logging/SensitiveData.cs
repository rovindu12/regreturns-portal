using System.Collections.Frozen;

namespace RegReturns.ServiceDefaults.Logging;

/// <summary>Decides which log properties carry secrets or personal data and must never reach a log sink.</summary>
internal static class SensitiveData
{
    /// <summary>The value written in place of a redacted property.</summary>
    public const string Mask = "***REDACTED***";

    private static readonly FrozenSet<string> SensitiveNames = new[]
    {
        "password", "secret", "clientsecret", "token", "accesstoken", "refreshtoken", "idtoken", "idtokenhint",
        "logouttoken", "code", "authorizationcode", "clientassertion", "credential", "credentials", "privatekey",
        "hmackey", "authorization", "cookie", "setcookie", "apikey", "email", "emailaddress", "phone", "phonenumber",
        "otp", "totp", "totpsecret", "passcode", "connectionstring",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> SensitiveAttributeNames = new[]
    {
        "PersonalDataAttribute", "SensitiveDataAttribute", "SecretAttribute",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Returns whether a property name denotes sensitive data, ignoring case, dashes and underscores.</summary>
    /// <param name="name">The property name.</param>
    /// <returns><see langword="true"/> if the value must be redacted.</returns>
    public static bool IsSensitiveName(string name) =>
        SensitiveNames.Contains(name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal));

    /// <summary>Returns whether a property is marked with an attribute named PersonalData, SensitiveData or Secret.</summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the value must be redacted.</returns>
    /// <remarks>Matching by attribute name keeps the Domain free of any logging dependency.</remarks>
    public static bool IsMarkedSensitive(System.Reflection.PropertyInfo property) =>
        property.GetCustomAttributes(inherit: true).Any(a => SensitiveAttributeNames.Contains(a.GetType().Name));
}
