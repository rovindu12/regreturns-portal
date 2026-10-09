using RegReturns.Domain.Common;
using RegReturns.Domain.Insights;

namespace RegReturns.Application.Insights;

/// <summary>
/// Writes the narrative of an insight from its payload (ADR 0030). The configured narrator is the Anthropic provider
/// (Infrastructure) or the <see cref="RuleBasedNarrator"/>; whatever the configured one fails with, the rule-based
/// writer answers instead.
/// </summary>
public interface IInsightNarrator
{
    /// <summary>Gets who writes the narrative.</summary>
    InsightProvider Provider { get; }

    /// <summary>Gets the model asked, such as <c>claude-opus-5-5</c>, or the rule set's name.</summary>
    string Model { get; }

    /// <summary>Gets a value indicating whether the narrator can be called at all (an API key is configured).</summary>
    bool IsConfigured { get; }

    /// <summary>Writes the narrative.</summary>
    /// <param name="request">The payload.</param>
    /// <param name="payloadJson">The payload exactly as serialised and hashed; this is what is sent.</param>
    /// <param name="cancellationToken">Cancels the call (the request was aborted).</param>
    /// <returns>The answer, or an <see cref="InsightErrors"/> error saying why there is none.</returns>
    Task<Result<NarratorAnswer>> NarrateAsync(InsightRequest request, string payloadJson, CancellationToken cancellationToken);
}

/// <summary>A narrator's answer.</summary>
/// <param name="Narrative">The narrative, cleaned.</param>
/// <param name="Model">The model that answered, as the provider reports it.</param>
/// <param name="InputTokens">Input tokens billed, or 0.</param>
/// <param name="OutputTokens">Output tokens billed (thinking included), or 0.</param>
public sealed record NarratorAnswer(InsightNarrative Narrative, string Model, long InputTokens, long OutputTokens);

/// <summary>Expected failures of insights and their providers.</summary>
public static class InsightErrors
{
    /// <summary>Only regulator reviewers and approvers use insights.</summary>
    public static readonly Error SupervisorsOnly = new(
        "Insight.SupervisorsOnly", "Only supervision reviewers and approvers can generate advisory insights.");

    /// <summary>No API key is configured.</summary>
    public static readonly Error NotConfigured = new("Insight.NotConfigured", "No API key is configured for the AI provider.");

    /// <summary>The provider did not answer in time.</summary>
    public static readonly Error Timeout = new("Insight.Timeout", "The AI provider did not answer in time.");

    /// <summary>The provider's rate limit was reached.</summary>
    public static readonly Error RateLimited = new("Insight.RateLimited", "The AI provider's rate limit was reached.");

    /// <summary>The provider did not accept the API key.</summary>
    public static readonly Error Unauthorized = new("Insight.Unauthorized", "The AI provider did not accept the API key.");

    /// <summary>The provider could not be reached or failed.</summary>
    public static readonly Error ProviderUnavailable = new("Insight.ProviderUnavailable", "The AI provider could not be reached.");

    /// <summary>The provider rejected the request.</summary>
    public static readonly Error ProviderRejected = new("Insight.ProviderRejected", "The AI provider rejected the request.");

    /// <summary>The model declined to answer.</summary>
    public static readonly Error Refused = new("Insight.Refused", "The AI model declined to answer.");

    /// <summary>The answer was cut short or not in the expected structure.</summary>
    public static readonly Error InvalidOutput = new("Insight.InvalidOutput", "The AI provider's answer could not be used.");

    private static readonly Dictionary<string, InsightFallbackReason> Reasons = new(StringComparer.Ordinal)
    {
        [NotConfigured.Code] = InsightFallbackReason.NotConfigured,
        [Timeout.Code] = InsightFallbackReason.Timeout,
        [RateLimited.Code] = InsightFallbackReason.RateLimited,
        [Unauthorized.Code] = InsightFallbackReason.Unauthorized,
        [ProviderUnavailable.Code] = InsightFallbackReason.ProviderUnavailable,
        [ProviderRejected.Code] = InsightFallbackReason.ProviderRejected,
        [Refused.Code] = InsightFallbackReason.Refused,
        [InvalidOutput.Code] = InsightFallbackReason.InvalidOutput,
        [InsightPayloadGuard.Rejected.Code] = InsightFallbackReason.PayloadRejected,
    };

    /// <summary>Returns the fallback reason a narrator's error stands for.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The reason; an unknown code counts as <see cref="InsightFallbackReason.ProviderUnavailable"/>.</returns>
    public static InsightFallbackReason ReasonOf(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Reasons.GetValueOrDefault(error.Code, InsightFallbackReason.ProviderUnavailable);
    }

    /// <summary>Returns what the panel says about a fallback.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns>A sentence.</returns>
    public static string Describe(InsightFallbackReason reason) => reason switch
    {
        InsightFallbackReason.NotConfigured => "No API key is configured for the AI provider.",
        InsightFallbackReason.Timeout => "The AI provider did not answer in time.",
        InsightFallbackReason.RateLimited => "The AI provider's rate limit was reached.",
        InsightFallbackReason.Unauthorized => "The AI provider did not accept the API key.",
        InsightFallbackReason.ProviderUnavailable => "The AI provider could not be reached.",
        InsightFallbackReason.ProviderRejected => "The AI provider rejected the request.",
        InsightFallbackReason.Refused => "The AI model declined to answer.",
        InsightFallbackReason.InvalidOutput => "The AI provider's answer could not be used.",
        InsightFallbackReason.PayloadRejected => "The data did not pass the privacy check, so nothing was sent.",
        _ => "The AI provider was not used.",
    };
}
