using RegReturns.Domain.Common;
using RegReturns.Domain.Insights;

namespace RegReturns.UnitTests.Domain;

public sealed class ReturnInsightTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);

    private static ReturnInsight Record(
        InsightProvider provider = InsightProvider.Anthropic,
        InsightFallbackReason? reason = null,
        int revision = 1,
        int durationMs = 1200,
        long auditSequence = 7) =>
        ReturnInsight.Record(
            Guid.CreateVersion7(), revision, Guid.CreateVersion7(), provider, "claude-opus-5-5", reason, "{\"a\":1}", "{\"b\":2}", durationMs, auditSequence, Now);

    [Fact]
    public void Digest_is_the_lower_case_hex_sha256_of_the_utf8_text()
    {
        ReturnInsight.Digest("abc").ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void Record_keeps_the_digests_of_the_payload_and_the_content()
    {
        var insight = Record();

        insight.InputSha256.ShouldBe(ReturnInsight.Digest("{\"a\":1}"));
        insight.OutputSha256.ShouldBe(ReturnInsight.Digest("{\"b\":2}"));
        (insight.CreatedAt, insight.AuditSequence, insight.IsFallback).ShouldBe((Now, 7L, false));
    }

    [Fact]
    public void Only_the_rule_based_writer_stands_in_for_a_provider()
    {
        Record(InsightProvider.RuleBased, InsightFallbackReason.Timeout).IsFallback.ShouldBeTrue();
        Should.Throw<DomainException>(() => Record(InsightProvider.Anthropic, InsightFallbackReason.Timeout));
    }

    [Fact]
    public void Revision_duration_and_audit_sequence_must_be_plausible()
    {
        Should.Throw<DomainException>(() => Record(revision: 0));
        Should.Throw<DomainException>(() => Record(durationMs: -1));
        Should.Throw<DomainException>(() => Record(auditSequence: 0));
    }
}
