using RegReturns.Application.Insights;
using RegReturns.Domain.Common;
using RegReturns.Domain.Insights;

namespace RegReturns.UnitTests.Application.Insights;

public sealed class InsightErrorsTests
{
    public static TheoryData<string, InsightFallbackReason> Reasons => new()
    {
        { InsightErrors.NotConfigured.Code, InsightFallbackReason.NotConfigured },
        { InsightErrors.Timeout.Code, InsightFallbackReason.Timeout },
        { InsightErrors.RateLimited.Code, InsightFallbackReason.RateLimited },
        { InsightErrors.Unauthorized.Code, InsightFallbackReason.Unauthorized },
        { InsightErrors.ProviderUnavailable.Code, InsightFallbackReason.ProviderUnavailable },
        { InsightErrors.ProviderRejected.Code, InsightFallbackReason.ProviderRejected },
        { InsightErrors.Refused.Code, InsightFallbackReason.Refused },
        { InsightErrors.InvalidOutput.Code, InsightFallbackReason.InvalidOutput },
        { InsightPayloadGuard.Rejected.Code, InsightFallbackReason.PayloadRejected },
    };

    [Theory]
    [MemberData(nameof(Reasons))]
    public void Each_provider_error_names_its_fallback_reason(string code, InsightFallbackReason reason)
    {
        InsightErrors.ReasonOf(new Error(code, "message")).ShouldBe(reason);
    }

    [Fact]
    public void An_unknown_error_counts_as_an_unavailable_provider()
    {
        InsightErrors.ReasonOf(new Error("Something.Else", "message")).ShouldBe(InsightFallbackReason.ProviderUnavailable);
    }

    [Fact]
    public void Every_reason_has_its_own_description()
    {
        var descriptions = Enum.GetValues<InsightFallbackReason>().Select(InsightErrors.Describe).ToList();

        descriptions.ShouldAllBe(d => d.EndsWith('.'));
        descriptions.Distinct().Count().ShouldBe(descriptions.Count);
    }
}
