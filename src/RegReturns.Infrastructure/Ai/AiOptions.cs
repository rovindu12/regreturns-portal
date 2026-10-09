using System.ComponentModel.DataAnnotations;

using RegReturns.Domain.Insights;

namespace RegReturns.Infrastructure.Ai;

/// <summary>Advisory insight settings (section <c>Ai</c>, ADR 0030).</summary>
public sealed class AiOptions : IValidatableObject
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ai";

    /// <summary>
    /// Gets or sets who writes insights: <see cref="InsightProvider.Anthropic"/> (the default; the rule-based writer
    /// stands in without a key or when a call fails) or <see cref="InsightProvider.RuleBased"/>, which never sends
    /// anything out.
    /// </summary>
    public InsightProvider Provider { get; set; } = InsightProvider.Anthropic;

    /// <summary>Gets the Anthropic settings.</summary>
    public AnthropicSettings Anthropic { get; } = new();

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(Provider))
        {
            yield return new ValidationResult($"{SectionName}:{nameof(Provider)} must be Anthropic or RuleBased.", [nameof(Provider)]);
        }

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(Anthropic, new ValidationContext(Anthropic), results, validateAllProperties: true);
        foreach (var result in results)
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(Anthropic)}: {result.ErrorMessage}",
                [.. result.MemberNames.Select(m => $"{nameof(Anthropic)}.{m}")]);
        }
    }
}

/// <summary>How to call Claude through the Anthropic API (section <c>Ai:Anthropic</c>).</summary>
public sealed class AnthropicSettings : IValidatableObject
{
    /// <summary>The model asked unless configured otherwise.</summary>
    public const string DefaultModel = "claude-opus-5-5";

    /// <summary>The effort levels the API accepts.</summary>
    public static readonly IReadOnlyList<string> EffortLevels = ["low", "medium", "high", "xhigh", "max"];

    /// <summary>
    /// Gets or sets the API key. Secret: set it in user-secrets or the environment (<c>Ai__Anthropic__ApiKey</c>), never
    /// in appsettings. Without one, every insight is rule-based and nothing is sent.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Gets or sets the model.</summary>
    [Required]
    [StringLength(64)]
    [RegularExpression(@"^[a-z0-9][a-z0-9.\-]*$", ErrorMessage = "The model must be a model id such as claude-opus-5-5.")]
    public string Model { get; set; } = DefaultModel;

    /// <summary>Gets or sets the effort: low, medium (the default), high, xhigh or max.</summary>
    [Required]
    public string Effort { get; set; } = "medium";

    /// <summary>Gets or sets the most tokens an answer may use, thinking included.</summary>
    [Range(1024, 64000)]
    public int MaxOutputTokens { get; set; } = 16000;

    /// <summary>Gets or sets how long to wait for an answer, retries included, before the rule-based writer answers.</summary>
    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets how often the SDK retries a rate-limited, failed or timed-out attempt within the timeout.</summary>
    [Range(0, 3)]
    public int MaxRetries { get; set; } = 1;

    /// <summary>Gets or sets another API address, such as a proxy; the SDK's default when not set.</summary>
    public Uri? BaseUrl { get; set; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!EffortLevels.Contains(Effort))
        {
            yield return new ValidationResult($"{nameof(Effort)} must be one of {string.Join(", ", EffortLevels)}.", [nameof(Effort)]);
        }

        if (BaseUrl is not null && (!BaseUrl.IsAbsoluteUri || BaseUrl.Scheme != Uri.UriSchemeHttps))
        {
            yield return new ValidationResult($"{nameof(BaseUrl)} must be an absolute https URL.", [nameof(BaseUrl)]);
        }
    }
}
