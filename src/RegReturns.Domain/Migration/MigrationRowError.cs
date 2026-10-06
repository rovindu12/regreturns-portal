using RegReturns.Domain.Common;

namespace RegReturns.Domain.Migration;

/// <summary>A legacy row that was rejected or superseded, with the reason, for the migration error report.</summary>
public sealed class MigrationRowError : Entity
{
    /// <summary>Maximum length of a file name.</summary>
    public const int FileNameMaxLength = 200;

    /// <summary>Maximum length of an error code.</summary>
    public const int CodeMaxLength = 64;

    /// <summary>Maximum length of a field code or legacy column name.</summary>
    public const int FieldMaxLength = 100;

    /// <summary>Maximum length of a message.</summary>
    public const int MessageMaxLength = 400;

    /// <summary>Maximum length of a kept source value; longer values are cut.</summary>
    public const int ValueMaxLength = 200;

    private MigrationRowError()
    {
        FileName = string.Empty;
        Code = string.Empty;
        Message = string.Empty;
    }

    /// <summary>Gets the migration run.</summary>
    public Guid MigrationRunId { get; }

    /// <summary>Gets the source file name.</summary>
    public string FileName { get; private set; }

    /// <summary>Gets the line in the source file, counting the header as line 1.</summary>
    public int LineNumber { get; private set; }

    /// <summary>Gets whether the row was rejected or superseded.</summary>
    public RowErrorKind Kind { get; private set; }

    /// <summary>Gets the stable error code, such as <c>Legacy.BadNumber</c>.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the field code or legacy column the error is about, if any.</summary>
    public string? Field { get; private set; }

    /// <summary>Gets what went wrong.</summary>
    public string Message { get; private set; }

    /// <summary>Gets the source value at fault, cut to <see cref="ValueMaxLength"/> characters, if any.</summary>
    public string? Value { get; private set; }

    /// <summary>Creates an error entry.</summary>
    /// <param name="fileName">The source file name.</param>
    /// <param name="lineNumber">The line in the source file.</param>
    /// <param name="kind">Rejected or superseded.</param>
    /// <param name="code">The stable error code.</param>
    /// <param name="message">What went wrong.</param>
    /// <param name="field">The field code or legacy column, if any.</param>
    /// <param name="value">The source value at fault, if any.</param>
    /// <returns>The entry.</returns>
    public static MigrationRowError Create(
        string fileName, int lineNumber, RowErrorKind kind, string code, string message, string? field = null, string? value = null) => new()
        {
            FileName = Guard.NotBlank(fileName, FileNameMaxLength),
            LineNumber = lineNumber > 0 ? lineNumber : throw new DomainException("A line number starts at 1."),
            Kind = kind,
            Code = Guard.NotBlank(code, CodeMaxLength),
            Message = Cut(Guard.NotBlank(message, int.MaxValue), MessageMaxLength)!,
            Field = string.IsNullOrWhiteSpace(field) ? null : Cut(field.Trim(), FieldMaxLength),
            Value = string.IsNullOrEmpty(value) ? null : Cut(value, ValueMaxLength),
        };

    private static string? Cut(string? text, int maxLength) =>
        text is null || text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength - 1), "…");
}
