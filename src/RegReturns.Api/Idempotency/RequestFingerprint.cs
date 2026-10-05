using System.Security.Cryptography;
using System.Text;

namespace RegReturns.Api.Idempotency;

/// <summary>
/// The fingerprint that tells a retry from a different request with the same idempotency key: the lower-case hex
/// SHA-256 of the method, the path with its query string, and the body bytes exactly as sent.
/// </summary>
internal static class RequestFingerprint
{
    /// <summary>Computes a fingerprint.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="pathAndQuery">The path base, path and query string, as received.</param>
    /// <param name="body">The request body.</param>
    /// <returns>64 lower-case hex characters.</returns>
    public static string Compute(string method, string pathAndQuery, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(pathAndQuery);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        // The line breaks keep the parts apart: no method and path can run into each other or into the body.
        hash.AppendData(Encoding.UTF8.GetBytes(string.Concat(method.ToUpperInvariant(), "\n", pathAndQuery, "\n")));
        hash.AppendData(body);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
