using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RegReturns.UnitTests.Identity;

/// <summary>
/// An in-memory private PKI for TLS tests: a trusted root, an intermediate, server certificates, and look-alike
/// CAs that must never be trusted. Certificates carry their private keys so they can serve TLS.
/// </summary>
public sealed class TestCertificateAuthority : IDisposable
{
    public const string HostName = "iam.valoria.test";

    private const string RootName = "CN=Valoria Test Root CA, O=Bank of Valoria (test)";
    private static readonly Oid ServerAuthentication = new("1.3.6.1.5.5.7.3.1");

    private readonly List<X509Certificate2> _created = [];

    public TestCertificateAuthority()
    {
        var now = DateTimeOffset.UtcNow;
        Root = Track(CreateCa(RootName, issuer: null, now.AddDays(-30), now.AddYears(5)));
        Intermediate = Track(CreateCa("CN=Valoria Test Issuing CA, O=Bank of Valoria (test)", Root, now.AddDays(-20), now.AddYears(2)));
        LeafFromRoot = CreateLeaf(Root, HostName, now.AddDays(-1), now.AddDays(30));
        LeafFromIntermediate = CreateLeaf(Intermediate, HostName, now.AddDays(-1), now.AddDays(30));
        LocalhostLeafFromIntermediate = CreateLeaf(Intermediate, "localhost", now.AddDays(-1), now.AddDays(30));
        ExpiredLeaf = CreateLeaf(Root, HostName, now.AddDays(-10), now.AddDays(-1));

        RogueRoot = Track(CreateCa("CN=Rogue Root CA, O=Not Valoria", issuer: null, now.AddDays(-30), now.AddYears(5)));
        LeafFromRogueRoot = CreateLeaf(RogueRoot, HostName, now.AddDays(-1), now.AddDays(30));

        // Same subject as the trusted root, different key: only the signature tells them apart.
        ImpostorRoot = Track(CreateCa(RootName, issuer: null, now.AddDays(-30), now.AddYears(5)));
        LeafFromImpostorRoot = CreateLeaf(ImpostorRoot, HostName, now.AddDays(-1), now.AddDays(30));
    }

    public X509Certificate2 Root { get; }

    public X509Certificate2 Intermediate { get; }

    public X509Certificate2 LeafFromRoot { get; }

    public X509Certificate2 LeafFromIntermediate { get; }

    public X509Certificate2 LocalhostLeafFromIntermediate { get; }

    public X509Certificate2 ExpiredLeaf { get; }

    public X509Certificate2 RogueRoot { get; }

    public X509Certificate2 LeafFromRogueRoot { get; }

    public X509Certificate2 ImpostorRoot { get; }

    public X509Certificate2 LeafFromImpostorRoot { get; }

    /// <summary>Returns a collection holding only the given roots.</summary>
    public static X509Certificate2Collection Trust(params X509Certificate2[] roots) => [.. roots];

    /// <summary>
    /// Builds the chain a TLS client would be handed for <paramref name="leaf"/> when the server also sends
    /// <paramref name="sentByServer"/>, without trusting anything (the platform does not know the private CA).
    /// </summary>
    public static X509Chain PresentedChain(X509Certificate2 leaf, params X509Certificate2[] sentByServer)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        chain.ChainPolicy.ExtraStore.AddRange(sentByServer);
        chain.Build(leaf);
        return chain;
    }

    public void Dispose()
    {
        foreach (var certificate in _created)
        {
            certificate.Dispose();
        }
    }

    private X509Certificate2 Track(X509Certificate2 certificate)
    {
        _created.Add(certificate);
        return certificate;
    }

    private static X509Certificate2 CreateCa(string subject, X509Certificate2? issuer, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        if (issuer is null)
        {
            return request.CreateSelfSigned(notBefore, notAfter);
        }

        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, includeKeyIdentifier: true, includeIssuerAndSerial: false));
        using var certificate = request.Create(issuer, notBefore, notAfter, NewSerialNumber());
        return certificate.CopyWithPrivateKey(key);
    }

    private X509Certificate2 CreateLeaf(X509Certificate2 issuer, string dnsName, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([ServerAuthentication], critical: false));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(dnsName);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, includeKeyIdentifier: true, includeIssuerAndSerial: false));

        using var certificate = request.Create(issuer, notBefore, notAfter, NewSerialNumber());
        using var withKey = certificate.CopyWithPrivateKey(key);

        // A PKCS#12 round trip gives a key every platform's TLS stack can use.
        return Track(X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pkcs12), password: null));
    }

    private static byte[] NewSerialNumber() => RandomNumberGenerator.GetBytes(16);
}
