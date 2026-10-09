using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RegReturns.Infrastructure.Identity.Wso2;

/// <summary>
/// Validates WSO2's HTTPS certificate against an explicitly configured set of root CAs only
/// (<see cref="X509ChainTrustMode.CustomRootTrust"/>). Host name, validity period and chain are all checked;
/// nothing is ever accepted just to make a connection work (ADR 0015).
/// </summary>
public sealed class Wso2CertificateValidator
{
    private readonly X509Certificate2Collection _trustedRoots;

    /// <summary>Initializes a new instance of the <see cref="Wso2CertificateValidator"/> class.</summary>
    /// <param name="trustedRoots">The only root certificates to trust.</param>
    public Wso2CertificateValidator(X509Certificate2Collection trustedRoots)
    {
        ArgumentNullException.ThrowIfNull(trustedRoots);
        if (trustedRoots.Count == 0)
        {
            throw new ArgumentException("At least one trusted root certificate is required.", nameof(trustedRoots));
        }

        _trustedRoots = trustedRoots;
    }

    /// <summary>Tells whether a PEM file can be read and holds at least one certificate.</summary>
    /// <param name="pemPath">Path to the PEM file.</param>
    /// <returns><see langword="true"/> when <see cref="FromPemFile"/> would succeed.</returns>
    public static bool CanLoad(string pemPath)
    {
        try
        {
            var roots = new X509Certificate2Collection();
            roots.ImportFromPemFile(pemPath);
            return roots.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Loads the trusted roots from a PEM file.</summary>
    /// <param name="pemPath">Path to a PEM file containing one or more CA certificates.</param>
    /// <returns>The validator.</returns>
    public static Wso2CertificateValidator FromPemFile(string pemPath)
    {
        if (!File.Exists(pemPath))
        {
            throw new FileNotFoundException($"The WSO2 trusted CA file '{pemPath}' does not exist.", pemPath);
        }

        var roots = new X509Certificate2Collection();
        roots.ImportFromPemFile(pemPath);
        return new Wso2CertificateValidator(roots);
    }

    /// <summary>Validates a server certificate presented during the TLS handshake.</summary>
    /// <param name="certificate">The server certificate.</param>
    /// <param name="presentedChain">The chain the server sent, used only as a source of intermediates.</param>
    /// <param name="sslPolicyErrors">The errors found by the platform's own validation.</param>
    /// <returns><see langword="true"/> if the certificate matches the host and chains to a trusted root.</returns>
    public bool Validate(X509Certificate? certificate, X509Chain? presentedChain, SslPolicyErrors sslPolicyErrors)
    {
        // The host name check and certificate presence come from the platform and must pass regardless of trust.
        if (certificate is null ||
            sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable) ||
            sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
        {
            return false;
        }

        using var leaf = new X509Certificate2(certificate);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(_trustedRoots);

        // The private development CA publishes no revocation list; expiry, signatures and the root are still checked.
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        if (presentedChain is not null)
        {
            foreach (var element in presentedChain.ChainElements.Skip(1))
            {
                chain.ChainPolicy.ExtraStore.Add(element.Certificate);
            }
        }

        return chain.Build(leaf);
    }
}
