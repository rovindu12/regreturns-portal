using System.Text;

using RegReturns.Api.Idempotency;
using RegReturns.Application.Idempotency;

namespace RegReturns.UnitTests.Api;

public sealed class IdempotencyRequestTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"returnType\":\"MLR\"}");

    [Fact]
    public void A_fingerprint_is_64_lower_case_hex_characters_and_repeatable()
    {
        var fingerprint = RequestFingerprint.Compute("POST", "/v1/submissions", Body);

        fingerprint.ShouldMatch("^[0-9a-f]{64}$");
        RequestFingerprint.Compute("post", "/v1/submissions", Body).ShouldBe(fingerprint);
    }

    [Fact]
    public void A_fingerprint_changes_with_the_body_the_path_or_the_method()
    {
        var fingerprint = RequestFingerprint.Compute("POST", "/v1/submissions", Body);

        RequestFingerprint.Compute("POST", "/v1/submissions", Encoding.UTF8.GetBytes("{\"returnType\":\"MDA\"}")).ShouldNotBe(fingerprint);
        RequestFingerprint.Compute("POST", "/v1/submissions?x=1", Body).ShouldNotBe(fingerprint);
        RequestFingerprint.Compute("PUT", "/v1/submissions", Body).ShouldNotBe(fingerprint);
    }

    [Fact]
    public void The_path_and_the_body_cannot_run_into_each_other()
    {
        RequestFingerprint.Compute("POST", "/v1/a", "b"u8).ShouldNotBe(RequestFingerprint.Compute("POST", "/v1/ab", []));
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, true)]
    [InlineData(400, true)]
    [InlineData(404, true)]
    [InlineData(422, true)]
    [InlineData(403, false)]
    [InlineData(409, false)]
    [InlineData(429, false)]
    [InlineData(500, false)]
    [InlineData(503, false)]
    public void Only_answers_a_retry_should_get_again_are_stored(int status, bool stored)
    {
        IdempotencyFilter.IsStored(status).ShouldBe(stored);
    }

    [Theory]
    [InlineData("8d3c1a52-6f0e-4b7a-9a51-2f0c6f3b9e10", true)]
    [InlineData("order-2027-03/MLR#1", true)]
    [InlineData("!", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("has space", false)]
    [InlineData("tab\there", false)]
    [InlineData("café", false)]
    public void Keys_are_one_to_255_visible_ascii_characters(string? key, bool valid)
    {
        IdempotencyErrors.IsValidKey(key).ShouldBe(valid);
    }

    [Fact]
    public void A_key_longer_than_255_characters_is_invalid()
    {
        IdempotencyErrors.IsValidKey(new string('k', IdempotencyErrors.KeyMaxLength)).ShouldBeTrue();
        IdempotencyErrors.IsValidKey(new string('k', IdempotencyErrors.KeyMaxLength + 1)).ShouldBeFalse();
    }
}
