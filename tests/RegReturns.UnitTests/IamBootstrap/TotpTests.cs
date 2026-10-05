extern alias IamBootstrapTool;

using System.Text;

using IamBootstrapTool::RegReturns.IamBootstrap;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class TotpTests
{
    // RFC 6238 appendix B uses the ASCII secret "12345678901234567890"; this is its base32 form.
    private const string RfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Code_matches_the_rfc_6238_sha1_test_vectors(long unixSeconds, string expected)
    {
        // The RFC publishes eight digits; WSO2 and authenticator apps use the last six of the same value.
        Totp.Code(RfcSecret, DateTimeOffset.FromUnixTimeSeconds(unixSeconds)).ShouldBe(expected);
    }

    [Fact]
    public void Code_stays_the_same_within_a_thirty_second_period()
    {
        Totp.Code(RfcSecret, DateTimeOffset.FromUnixTimeSeconds(1111111110))
            .ShouldBe(Totp.Code(RfcSecret, DateTimeOffset.FromUnixTimeSeconds(1111111139)));
    }

    [Fact]
    public void Code_changes_with_the_next_period()
    {
        Totp.Code(RfcSecret, DateTimeOffset.FromUnixTimeSeconds(1111111139))
            .ShouldNotBe(Totp.Code(RfcSecret, DateTimeOffset.FromUnixTimeSeconds(1111111140)));
    }

    [Fact]
    public void Code_ignores_the_case_and_padding_of_the_secret()
    {
        Totp.Code("gezdgnbvgy3tqojqgezdgnbvgy3tqojq====", DateTimeOffset.FromUnixTimeSeconds(59)).ShouldBe("287082");
    }

    [Fact]
    public void Code_does_not_depend_on_the_offset_of_the_time()
    {
        var utc = DateTimeOffset.FromUnixTimeSeconds(1234567890);

        Totp.Code(RfcSecret, utc.ToOffset(TimeSpan.FromHours(5.5))).ShouldBe("005924");
    }

    [Fact]
    public void Base32_secret_of_the_rfc_decodes_to_its_ascii_bytes()
    {
        Encoding.ASCII.GetString(Totp.FromBase32(RfcSecret)).ShouldBe("12345678901234567890");
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("MY======", "f")]
    [InlineData("MZXQ====", "fo")]
    [InlineData("MZXW6===", "foo")]
    [InlineData("MZXW6YQ=", "foob")]
    [InlineData("MZXW6YTB", "fooba")]
    [InlineData("MZXW6YTBOI======", "foobar")]
    public void Base32_matches_the_rfc_4648_test_vectors(string encoded, string expected)
    {
        Encoding.ASCII.GetString(Totp.FromBase32(encoded)).ShouldBe(expected);
    }

    [Fact]
    public void Base32_padding_is_optional()
    {
        Encoding.ASCII.GetString(Totp.FromBase32("MZXW6YTBOI")).ShouldBe("foobar");
    }

    [Fact]
    public void Base32_accepts_lower_case()
    {
        Encoding.ASCII.GetString(Totp.FromBase32("mzxw6ytboi")).ShouldBe("foobar");
    }

    [Fact]
    public void Base32_ignores_spaces_between_groups()
    {
        Encoding.ASCII.GetString(Totp.FromBase32("MZXW 6YTB OI")).ShouldBe("foobar");
    }

    [Theory]
    [InlineData("MZXW1")]
    [InlineData("MZXW0")]
    [InlineData("MZXW8")]
    [InlineData("MZXW6!")]
    [InlineData("MZXW-6YTB")]
    [InlineData("MZXW\t6YTB")]
    public void Base32_rejects_characters_outside_the_alphabet(string encoded)
    {
        Should.Throw<FormatException>(() => Totp.FromBase32(encoded));
    }

    [Fact]
    public void Base32_rejects_null()
    {
        Should.Throw<ArgumentNullException>(() => Totp.FromBase32(null!));
    }

    [Fact]
    public void Secret_is_read_from_an_otpauth_uri()
    {
        Totp.SecretFromUri("otpauth://totp/RegReturns:approver.mfa?secret=JBSWY3DPEHPK3PXP&issuer=RegReturns")
            .ShouldBe("JBSWY3DPEHPK3PXP");
    }

    [Fact]
    public void Secret_is_found_after_other_parameters()
    {
        Totp.SecretFromUri("otpauth://totp/approver.mfa?issuer=Bank%20of%20Valoria&period=30&secret=JBSWY3DPEHPK3PXP")
            .ShouldBe("JBSWY3DPEHPK3PXP");
    }

    [Fact]
    public void Secret_is_unescaped()
    {
        Totp.SecretFromUri("otpauth://totp/approver.mfa?secret=JBSW%20Y3DP").ShouldBe("JBSW Y3DP");
    }

    [Fact]
    public void Secret_is_null_when_the_uri_has_none()
    {
        Totp.SecretFromUri("otpauth://totp/approver.mfa?issuer=RegReturns").ShouldBeNull();
    }

    [Fact]
    public void Secret_is_null_when_the_uri_has_no_query()
    {
        Totp.SecretFromUri("otpauth://totp/approver.mfa").ShouldBeNull();
    }

    [Fact]
    public void Parameters_that_only_start_with_secret_are_not_the_secret()
    {
        Totp.SecretFromUri("otpauth://totp/approver.mfa?secretive=nope&secret=JBSWY3DPEHPK3PXP").ShouldBe("JBSWY3DPEHPK3PXP");
    }

    [Fact]
    public void Secret_read_from_a_uri_produces_the_same_codes()
    {
        var secret = Totp.SecretFromUri($"otpauth://totp/approver.mfa?secret={RfcSecret}&issuer=RegReturns")!;

        Totp.Code(secret, DateTimeOffset.FromUnixTimeSeconds(59)).ShouldBe("287082");
    }
}
