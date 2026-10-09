using System.Text.RegularExpressions;

using QRCoder;

namespace RegReturns.Web.Demo;

/// <summary>
/// The enrolment link and QR code of a demo account's published TOTP secret (ADR 0031), for an authenticator app.
/// WSO2 uses the standard parameters (SHA-1, six digits, 30 seconds), so the link names only the secret.
/// </summary>
public static partial class TotpQrCode
{
    /// <summary>The issuer shown in authenticator apps.</summary>
    public const string Issuer = "RegReturns demo";

    /// <summary>Returns the secret in canonical Base32 (upper case, no spaces or padding), or <see langword="null"/> if it is not Base32.</summary>
    /// <param name="secret">The configured secret.</param>
    /// <returns>The canonical secret, or <see langword="null"/>.</returns>
    public static string? Canonical(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var canonical = secret.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        return Base32().IsMatch(canonical) ? canonical : null;
    }

    /// <summary>Builds the <c>otpauth://</c> link for an account.</summary>
    /// <param name="userName">The account's user name, shown as the label.</param>
    /// <param name="secret">A canonical Base32 secret.</param>
    /// <returns>The link.</returns>
    public static Uri Link(string userName, string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var label = Uri.EscapeDataString($"{Issuer}:{userName}");
        return new Uri($"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(Issuer)}");
    }

    /// <summary>Renders the link as a QR code in SVG that scales with its container.</summary>
    /// <param name="link">The <c>otpauth://</c> link.</param>
    /// <returns>The SVG markup. It holds only the code's squares, no text from the link.</returns>
    public static string Svg(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(link.AbsoluteUri, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(4, "#000000", "#ffffff", drawQuietZones: true, SvgQRCode.SizingMode.ViewBoxAttribute);
    }

    [GeneratedRegex("^[A-Z2-7]{16,128}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Base32();
}
