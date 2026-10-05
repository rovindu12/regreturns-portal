using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

using RegReturns.Domain.Auditing;

namespace RegReturns.Infrastructure.Auditing;

/// <summary>Computes and checks the keyed hash of audit entries (HMAC-SHA256, lower-case hex).</summary>
/// <param name="options">The audit options holding the key.</param>
public sealed class AuditHasher(IOptions<AuditOptions> options)
{
    private readonly byte[] _key = options.Value.GetKeyBytes();

    /// <summary>Hashes a canonical entry string.</summary>
    /// <param name="canonical">The output of <see cref="AuditEntry.ToCanonicalString"/>.</param>
    /// <returns>The hex-encoded HMAC.</returns>
    public string Compute(string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        return Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>Returns whether an entry's stored hash matches its contents, in constant time.</summary>
    /// <param name="entry">The stored entry.</param>
    /// <returns><see langword="true"/> if the entry has not been altered.</returns>
    public bool Verify(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var expected = Encoding.ASCII.GetBytes(Compute(entry.ToCanonicalString()));
        var actual = Encoding.ASCII.GetBytes(entry.Hash);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
