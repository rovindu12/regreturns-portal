using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

using RegReturns.Infrastructure.Identity.Wso2;

namespace RegReturns.UnitTests.Identity;

public sealed class Wso2CertificateValidatorTests(TestCertificateAuthority pki) : IClassFixture<TestCertificateAuthority>
{
    // The platform does not know the private CA, so it always reports a chain error for these certificates.
    private const SslPolicyErrors UntrustedByPlatform = SslPolicyErrors.RemoteCertificateChainErrors;

    private readonly Wso2CertificateValidator _validator = new(TestCertificateAuthority.Trust(pki.Root));

    [Fact]
    public void Certificate_issued_by_the_trusted_root_is_accepted()
    {
        _validator.Validate(pki.LeafFromRoot, presentedChain: null, UntrustedByPlatform).ShouldBeTrue();
    }

    [Fact]
    public void Certificate_without_platform_errors_is_still_checked_against_the_trusted_root()
    {
        _validator.Validate(pki.LeafFromRogueRoot, presentedChain: null, SslPolicyErrors.None).ShouldBeFalse();
    }

    [Fact]
    public void Intermediate_sent_by_the_server_completes_the_chain()
    {
        using var presented = TestCertificateAuthority.PresentedChain(pki.LeafFromIntermediate, pki.Intermediate);
        presented.ChainElements.Count.ShouldBeGreaterThanOrEqualTo(2);

        _validator.Validate(pki.LeafFromIntermediate, presented, UntrustedByPlatform).ShouldBeTrue();
    }

    [Fact]
    public void Certificate_from_an_intermediate_the_server_did_not_send_is_rejected()
    {
        _validator.Validate(pki.LeafFromIntermediate, presentedChain: null, UntrustedByPlatform).ShouldBeFalse();
    }

    [Fact]
    public void Certificate_from_an_untrusted_root_is_rejected()
    {
        _validator.Validate(pki.LeafFromRogueRoot, presentedChain: null, UntrustedByPlatform).ShouldBeFalse();
    }

    [Fact]
    public void Root_sent_by_the_server_does_not_become_trusted()
    {
        using var presented = TestCertificateAuthority.PresentedChain(pki.LeafFromRogueRoot, pki.RogueRoot);

        _validator.Validate(pki.LeafFromRogueRoot, presented, UntrustedByPlatform).ShouldBeFalse();
    }

    [Fact]
    public void Certificate_from_an_impostor_root_with_the_trusted_name_is_rejected()
    {
        _validator.Validate(pki.LeafFromImpostorRoot, presentedChain: null, UntrustedByPlatform).ShouldBeFalse();
    }

    [Fact]
    public void Expired_certificate_is_rejected()
    {
        _validator.Validate(pki.ExpiredLeaf, presentedChain: null, UntrustedByPlatform).ShouldBeFalse();
    }

    [Fact]
    public void Host_name_mismatch_is_rejected_even_for_a_trusted_certificate()
    {
        _validator.Validate(pki.LeafFromRoot, presentedChain: null, SslPolicyErrors.RemoteCertificateNameMismatch).ShouldBeFalse();
    }

    [Fact]
    public void Missing_certificate_is_rejected()
    {
        _validator.Validate(certificate: null, presentedChain: null, SslPolicyErrors.None).ShouldBeFalse();
    }

    [Fact]
    public void Certificate_reported_as_not_available_is_rejected()
    {
        _validator.Validate(pki.LeafFromRoot, presentedChain: null, SslPolicyErrors.RemoteCertificateNotAvailable).ShouldBeFalse();
    }

    [Fact]
    public void Empty_trusted_root_collection_is_refused()
    {
        Should.Throw<ArgumentException>(() => new Wso2CertificateValidator([]));
    }

    [Fact]
    public void Null_trusted_root_collection_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => new Wso2CertificateValidator(null!));
    }

    [Fact]
    public void FromPemFile_trusts_the_certificates_in_the_file()
    {
        var path = WritePem(pki.Root);
        try
        {
            var validator = Wso2CertificateValidator.FromPemFile(path);

            validator.Validate(pki.LeafFromRoot, presentedChain: null, UntrustedByPlatform).ShouldBeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromPemFile_trusts_every_root_in_a_bundle()
    {
        var path = WritePem(pki.RogueRoot, pki.Root);
        try
        {
            var validator = Wso2CertificateValidator.FromPemFile(path);

            validator.Validate(pki.LeafFromRoot, presentedChain: null, UntrustedByPlatform).ShouldBeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromPemFile_does_not_trust_roots_outside_the_file()
    {
        var path = WritePem(pki.Root);
        try
        {
            var validator = Wso2CertificateValidator.FromPemFile(path);

            validator.Validate(pki.LeafFromRogueRoot, presentedChain: null, UntrustedByPlatform).ShouldBeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromPemFile_throws_for_a_missing_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pem");

        Should.Throw<FileNotFoundException>(() => Wso2CertificateValidator.FromPemFile(path)).FileName.ShouldBe(path);
    }

    [Fact]
    public void CanLoad_accepts_a_file_with_a_certificate()
    {
        var path = WritePem(pki.Root);
        try
        {
            Wso2CertificateValidator.CanLoad(path).ShouldBeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CanLoad_refuses_a_missing_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pem");

        Wso2CertificateValidator.CanLoad(path).ShouldBeFalse();
    }

    [Fact]
    public void CanLoad_refuses_a_file_without_a_certificate()
    {
        var path = WritePem();
        try
        {
            Wso2CertificateValidator.CanLoad(path).ShouldBeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WritePem(params X509Certificate2[] certificates)
    {
        var path = Path.Combine(Path.GetTempPath(), $"regreturns-ca-{Guid.NewGuid():N}.pem");
        File.WriteAllText(path, string.Concat(certificates.Select(c => c.ExportCertificatePem() + "\n")));
        return path;
    }
}
