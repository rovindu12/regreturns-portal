using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;

namespace RegReturns.IamBootstrap;

/// <summary>Time-based one-time passwords (RFC 6238) as WSO2's TOTP authenticator and authenticator apps compute them.</summary>
internal static class Totp
{
    /// <summary>Seconds each code is valid for.</summary>
    public const int PeriodSeconds = 30;

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Computes the six-digit code for a base32 secret at a point in time.</summary>
    /// <param name="base32Secret">The shared secret, base32 (padding and case are ignored).</param>
    /// <param name="at">The time.</param>
    /// <returns>The code, zero-padded to six digits.</returns>
    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "RFC 6238 and every authenticator app use HMAC-SHA1 for TOTP; WSO2 verifies codes the same way.")]
    public static string Code(string base32Secret, DateTimeOffset at)
    {
        Span<byte> counter = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counter, at.ToUnixTimeSeconds() / PeriodSeconds);
        var hash = HMACSHA1.HashData(FromBase32(base32Secret), counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>Decodes RFC 4648 base32.</summary>
    /// <param name="value">The base32 text; padding, spaces and case are ignored.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="FormatException">The text contains a character outside the base32 alphabet.</exception>
    public static byte[] FromBase32(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var output = new List<byte>(value.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in value)
        {
            if (c is '=' or ' ')
            {
                continue;
            }

            var index = Base32Alphabet.IndexOf(char.ToUpperInvariant(c), StringComparison.Ordinal);
            if (index < 0)
            {
                throw new FormatException("The secret is not valid base32.");
            }

            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        return [.. output];
    }

    /// <summary>Reads the <c>secret</c> parameter of an <c>otpauth://</c> URI.</summary>
    /// <param name="otpAuthUri">The URI.</param>
    /// <returns>The base32 secret, or <see langword="null"/> when absent.</returns>
    public static string? SecretFromUri(string otpAuthUri)
    {
        var query = new Uri(otpAuthUri).Query.TrimStart('?');
        return query.Split('&')
            .Select(pair => pair.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0] == "secret")
            .Select(parts => Uri.UnescapeDataString(parts[1]))
            .FirstOrDefault();
    }
}
