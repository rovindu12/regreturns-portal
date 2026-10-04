using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Caching.Distributed;

namespace RegReturns.Web.Identity;

/// <summary>
/// WSO2 session ids (<c>sid</c>) that were ended by back-channel logout. The cookie handler refuses any portal session
/// carrying one of them, so signing out of WSO2 (or an administrator ending a session there) ends the portal session.
/// </summary>
public interface ISessionDenyList
{
    /// <summary>Marks a WSO2 session as ended.</summary>
    /// <param name="sessionId">The WSO2 session id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the session is denied.</returns>
    Task DenyAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>Returns <see langword="true"/> if the WSO2 session has been ended.</summary>
    /// <param name="sessionId">The WSO2 session id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Whether the session is denied.</returns>
    Task<bool> IsDeniedAsync(string sessionId, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps the deny list in an <see cref="IDistributedCache"/> (in memory for a single instance today; Redis or SQL
/// Server when the portal runs on several instances). Entries outlive the longest possible portal session, and keys
/// hold a hash of the session id rather than the id itself.
/// </summary>
/// <param name="cache">The distributed cache.</param>
public sealed class DistributedSessionDenyList(IDistributedCache cache) : ISessionDenyList
{
    private const string KeyPrefix = "regreturns:ended-session:";
    private static readonly byte[] Marker = [1];

    /// <summary>Gets how long a denied session id is remembered: the absolute session lifetime plus a margin.</summary>
    public static TimeSpan Retention { get; } = PortalSession.AbsoluteLifetime + TimeSpan.FromMinutes(10);

    /// <inheritdoc />
    public Task DenyAsync(string sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return cache.SetAsync(
            Key(sessionId), Marker, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Retention }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsDeniedAsync(string sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return await cache.GetAsync(Key(sessionId), cancellationToken) is not null;
    }

    private static string Key(string sessionId) =>
        KeyPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)));
}
