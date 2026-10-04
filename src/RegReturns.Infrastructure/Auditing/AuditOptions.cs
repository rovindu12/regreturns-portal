using System.ComponentModel.DataAnnotations;

namespace RegReturns.Infrastructure.Auditing;

/// <summary>Audit chain settings (section <c>Audit</c>).</summary>
public sealed class AuditOptions : IValidatableObject
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Audit";

    /// <summary>Minimum key size in bytes (256 bits, the HMAC-SHA256 block-strength recommendation).</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>
    /// Gets or sets the base64 HMAC key. It lives only in user-secrets or the environment, never in the database,
    /// so someone with database access alone cannot re-forge the chain.
    /// </summary>
    [Required]
    public string? HmacKey { get; set; }

    /// <summary>Decodes <see cref="HmacKey"/>.</summary>
    /// <returns>The key bytes.</returns>
    public byte[] GetKeyBytes() => Convert.FromBase64String(HmacKey ?? string.Empty);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        byte[] key;
        try
        {
            key = GetKeyBytes();
        }
        catch (FormatException)
        {
            key = [];
        }

        if (key.Length < MinimumKeyBytes)
        {
            yield return new ValidationResult(
                $"{SectionName}:{nameof(HmacKey)} must be base64 for at least {MinimumKeyBytes} random bytes " +
                "(for example `openssl rand -base64 32`).",
                [nameof(HmacKey)]);
        }
    }
}
