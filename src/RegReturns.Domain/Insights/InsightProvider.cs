namespace RegReturns.Domain.Insights;

/// <summary>Who wrote the narrative of a return insight (ADR 0030).</summary>
public enum InsightProvider
{
    /// <summary>A Claude model through the Anthropic API.</summary>
    Anthropic = 1,

    /// <summary>The application's fixed rules; nothing left the system.</summary>
    RuleBased = 2,
}

/// <summary>Why the rule-based writer answered instead of the configured AI provider.</summary>
public enum InsightFallbackReason
{
    /// <summary>No API key is configured.</summary>
    NotConfigured = 1,

    /// <summary>The provider did not answer within the configured time.</summary>
    Timeout = 2,

    /// <summary>The provider refused the call because of its rate limits.</summary>
    RateLimited = 3,

    /// <summary>The provider did not accept the API key.</summary>
    Unauthorized = 4,

    /// <summary>The provider could not be reached or failed (network error or a 5xx answer).</summary>
    ProviderUnavailable = 5,

    /// <summary>The provider rejected the request, for example an unknown model.</summary>
    ProviderRejected = 6,

    /// <summary>The model declined to answer.</summary>
    Refused = 7,

    /// <summary>The answer was cut short or did not match the expected structure.</summary>
    InvalidOutput = 8,

    /// <summary>The payload failed the guard, so it was never sent.</summary>
    PayloadRejected = 9,
}
