namespace RegReturns.Domain.Migration;

/// <summary>A legacy file a migration run read, identified by its SHA-256 so a later run can show it read the same data.</summary>
public sealed record MigrationSourceFile
{
    /// <summary>Maximum length of a file name.</summary>
    public const int FileNameMaxLength = MigrationRowError.FileNameMaxLength;

    /// <summary>Length of a hex SHA-256 digest.</summary>
    public const int Sha256Length = 64;

    /// <summary>Gets the file name.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the return type the file holds.</summary>
    public required string ReturnTypeCode { get; init; }

    /// <summary>Gets the lowercase hex SHA-256 of the file's bytes.</summary>
    public required string Sha256 { get; init; }

    /// <summary>Gets the number of data rows, blank rows included.</summary>
    public required int Rows { get; init; }
}
