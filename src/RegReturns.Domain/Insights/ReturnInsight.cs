using System.Security.Cryptography;
using System.Text;

using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;

namespace RegReturns.Domain.Insights;

/// <summary>
/// An advisory note on one revision of a return, written for a supervisor by an AI provider or by fixed rules
/// (ADR 0030). It keeps the payload the provider received and the content the reviewer read, with the SHA-256 of
/// each. Not audited as data: its generation is an <see cref="AuditAction.InsightGenerated"/> event that carries both
/// digests, so a changed payload or content no longer matches the chain.
/// </summary>
[NotAudited]
public sealed class ReturnInsight : Entity
{
    /// <summary>Maximum length of the payload (JSON).</summary>
    public const int PayloadMaxLength = 100_000;

    /// <summary>Maximum length of the content (JSON).</summary>
    public const int ContentMaxLength = 20_000;

    /// <summary>Maximum length of a model name.</summary>
    public const int ModelMaxLength = 64;

    /// <summary>Length of a hex SHA-256 digest.</summary>
    public const int Sha256Length = 64;

    private ReturnInsight()
    {
        Model = string.Empty;
        Payload = string.Empty;
        InputSha256 = string.Empty;
        Content = string.Empty;
        OutputSha256 = string.Empty;
    }

    /// <summary>Gets the return the insight is about.</summary>
    public Guid SubmissionId { get; private set; }

    /// <summary>Gets the revision of the return it describes.</summary>
    public int Revision { get; private set; }

    /// <summary>Gets the supervisor who asked for it.</summary>
    public Guid RequestedByUserId { get; private set; }

    /// <summary>Gets when it was generated.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets who wrote the narrative.</summary>
    public InsightProvider Provider { get; private set; }

    /// <summary>Gets the model that wrote it, such as <c>claude-opus-5-5</c>, or the rule set's name.</summary>
    public string Model { get; private set; }

    /// <summary>Gets why the rule-based writer answered instead of the configured provider, if it did.</summary>
    public InsightFallbackReason? FallbackReason { get; private set; }

    /// <summary>Gets the payload, as sent to the provider (or as it would have been sent).</summary>
    public string Payload { get; private set; }

    /// <summary>Gets the lowercase hex SHA-256 of the payload's UTF-8 bytes.</summary>
    public string InputSha256 { get; private set; }

    /// <summary>Gets the content shown to the reviewer (JSON).</summary>
    public string Content { get; private set; }

    /// <summary>Gets the lowercase hex SHA-256 of the content's UTF-8 bytes.</summary>
    public string OutputSha256 { get; private set; }

    /// <summary>Gets how long the generation took, provider call included, in milliseconds.</summary>
    public int DurationMs { get; private set; }

    /// <summary>Gets the sequence of the audit entry that recorded the generation.</summary>
    public long AuditSequence { get; private set; }

    /// <summary>Gets a value indicating whether the rule-based writer stood in for the configured provider.</summary>
    public bool IsFallback => FallbackReason is not null;

    /// <summary>Records a generated insight.</summary>
    /// <param name="submissionId">The return.</param>
    /// <param name="revision">The revision described.</param>
    /// <param name="requestedByUserId">The supervisor who asked.</param>
    /// <param name="provider">Who wrote the narrative.</param>
    /// <param name="model">The model or rule set.</param>
    /// <param name="fallbackReason">Why the rule-based writer answered instead of the configured provider, if it did.</param>
    /// <param name="payload">The payload (JSON).</param>
    /// <param name="content">The content (JSON).</param>
    /// <param name="durationMs">How long it took.</param>
    /// <param name="auditSequence">The audit entry that recorded it.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The insight.</returns>
    public static ReturnInsight Record(
        Guid submissionId,
        int revision,
        Guid requestedByUserId,
        InsightProvider provider,
        string model,
        InsightFallbackReason? fallbackReason,
        string payload,
        string content,
        int durationMs,
        long auditSequence,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(provider))
        {
            throw new DomainException($"Unknown insight provider {provider}.");
        }

        if (fallbackReason is { } reason && (!Enum.IsDefined(reason) || provider != InsightProvider.RuleBased))
        {
            throw new DomainException("Only the rule-based writer stands in for a provider.");
        }

        return new ReturnInsight
        {
            SubmissionId = Guard.NotEmpty(submissionId),
            Revision = revision >= 1 ? revision : throw new DomainException("A revision starts at 1."),
            RequestedByUserId = Guard.NotEmpty(requestedByUserId),
            CreatedAt = now,
            Provider = provider,
            Model = Guard.NotBlank(model, ModelMaxLength),
            FallbackReason = fallbackReason,
            Payload = Guard.NotBlank(payload, PayloadMaxLength),
            InputSha256 = Digest(payload),
            Content = Guard.NotBlank(content, ContentMaxLength),
            OutputSha256 = Digest(content),
            DurationMs = durationMs >= 0 ? durationMs : throw new DomainException("A duration cannot be negative."),
            AuditSequence = auditSequence >= 1 ? auditSequence : throw new DomainException("An audit sequence starts at 1."),
        };
    }

    /// <summary>Returns the lowercase hex SHA-256 of a text's UTF-8 bytes, as recorded for payloads and contents.</summary>
    /// <param name="text">The text.</param>
    /// <returns>64 lowercase hex characters.</returns>
    public static string Digest(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
