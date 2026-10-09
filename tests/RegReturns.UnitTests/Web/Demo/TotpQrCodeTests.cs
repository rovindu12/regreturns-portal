using RegReturns.Web.Demo;

namespace RegReturns.UnitTests.Web.Demo;

public sealed class TotpQrCodeTests
{
    private const string Secret = "JBSWY3DPEHPK3PXPJBSWY3DP";

    [Theory]
    [InlineData("JBSWY3DPEHPK3PXPJBSWY3DP")]
    [InlineData("jbsw y3dp ehpk 3pxp jbsw y3dp")]
    [InlineData("JBSWY3DPEHPK3PXPJBSWY3DP====")]
    public void Secrets_are_put_in_canonical_base32(string configured)
    {
        TotpQrCode.Canonical(configured).ShouldBe(Secret);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("JBSWY3DP")]
    [InlineData("JBSWY3DPEHPK3PXP0189")]
    [InlineData("JBSWY3DPEHPK3PXP&issuer=x")]
    public void Anything_that_is_not_a_base32_secret_is_left_out(string? configured)
    {
        TotpQrCode.Canonical(configured).ShouldBeNull();
    }

    [Fact]
    public void The_link_names_the_issuer_the_account_and_the_secret()
    {
        TotpQrCode.Link("approver.mfa", Secret).AbsoluteUri
            .ShouldBe($"otpauth://totp/RegReturns%20demo%3Aapprover.mfa?secret={Secret}&issuer=RegReturns%20demo");
    }

    [Fact]
    public void The_qr_code_is_a_scalable_svg_without_the_secret_in_text()
    {
        var svg = TotpQrCode.Svg(TotpQrCode.Link("approver.mfa", Secret));

        svg.ShouldStartWith("<svg");
        svg.ShouldContain("viewBox=");
        svg.ShouldNotContain(Secret);
        svg.ShouldNotContain("<script", Case.Insensitive);
    }
}
