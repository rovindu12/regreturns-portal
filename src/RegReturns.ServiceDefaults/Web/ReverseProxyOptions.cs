using System.Net;

namespace RegReturns.ServiceDefaults.Web;

/// <summary>
/// The reverse proxy in front of the app (section <c>ReverseProxy</c>, ADR 0034). Behind Caddy the app serves plain
/// HTTP on the internal network: the proxy terminates TLS, redirects HTTP to HTTPS and says in <c>X-Forwarded-Proto</c>
/// and <c>X-Forwarded-For</c> how the client connected. Those headers are believed only from the networks listed here.
/// </summary>
public sealed class ReverseProxyOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ReverseProxy";

    /// <summary>
    /// Gets or sets the networks the proxy connects from, in CIDR notation (for example <c>172.30.0.0/24</c>, the edge
    /// network in <c>docker-compose.prod.yml</c>). Empty (the default) means no proxy: forwarded headers are ignored and
    /// the app redirects HTTP to HTTPS itself. Empty by default because configuration binding appends to a list's
    /// initial items.
    /// </summary>
    public IList<string> KnownNetworks { get; set; } = [];

    /// <summary>Gets a value indicating whether the app runs behind the proxy.</summary>
    public bool BehindProxy => KnownNetworks.Count > 0;

    /// <summary>Returns the parsed networks.</summary>
    /// <returns>The networks.</returns>
    /// <exception cref="FormatException">An entry is not a network in CIDR notation.</exception>
    public IReadOnlyList<IPNetwork> Networks() => [.. KnownNetworks.Select(n => IPNetwork.Parse(n.Trim()))];

    /// <summary>Gets a value indicating whether every entry is a network in CIDR notation.</summary>
    /// <returns><see langword="true"/> if all entries parse.</returns>
    public bool AreValid() => KnownNetworks.All(n => IPNetwork.TryParse(n?.Trim(), out _));
}
