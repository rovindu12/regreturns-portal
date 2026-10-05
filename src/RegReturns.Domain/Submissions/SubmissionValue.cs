using RegReturns.Domain.Common;
using RegReturns.Domain.Templates;

namespace RegReturns.Domain.Submissions;

/// <summary>One field value in a submission, kept as entered plus a parsed number for reporting.</summary>
public sealed class SubmissionValue : Entity
{
    /// <summary>Maximum length of a raw value.</summary>
    public const int RawValueMaxLength = 400;

    private SubmissionValue()
    {
        FieldCode = string.Empty;
    }

    /// <summary>Gets the owning submission id.</summary>
    public Guid SubmissionId { get; }

    /// <summary>Gets the template field code.</summary>
    public string FieldCode { get; private set; }

    /// <summary>Gets the value exactly as entered or uploaded (trimmed), or <see langword="null"/> if blank.</summary>
    public string? RawValue { get; private set; }

    /// <summary>
    /// Gets the parsed number for numeric fields, or <see langword="null"/> if blank or not valid for the field
    /// (<see cref="FieldValueParser"/>), so reports only ever sum values the validation engine accepts.
    /// </summary>
    public decimal? NumericValue { get; private set; }

    internal static SubmissionValue Create(TemplateField field, string? raw)
    {
        var value = new SubmissionValue { FieldCode = field.Code };
        value.Update(field, raw);
        return value;
    }

    /// <summary>Returns a raw value as it is stored: trimmed, or <see langword="null"/> if blank.</summary>
    /// <param name="raw">The raw value.</param>
    /// <returns>The stored form.</returns>
    public static string? Normalize(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    internal void Update(TemplateField field, string? raw)
    {
        var trimmed = Normalize(raw);
        if (trimmed?.Length > RawValueMaxLength)
        {
            throw new DomainException($"Value for {field.Code} must be at most {RawValueMaxLength} characters.");
        }

        RawValue = trimmed;
        NumericValue = field.IsNumeric ? FieldValueParser.Parse(field, trimmed).Number : null;
    }
}
