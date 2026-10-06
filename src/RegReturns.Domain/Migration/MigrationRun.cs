using RegReturns.Domain.Common;

namespace RegReturns.Domain.Migration;

/// <summary>
/// One run of the legacy migration (ADR 0029): which files it read, whether it was a dry run, how every source row was
/// accounted for, and the rows it rejected or found superseded. Dry runs are recorded too, so rehearsals leave a trail.
/// </summary>
public sealed class MigrationRun : Entity
{
    /// <summary>Maximum length of the source description.</summary>
    public const int SourceMaxLength = 260;

    /// <summary>Length of a hex SHA-256 digest.</summary>
    public const int Sha256Length = MigrationSourceFile.Sha256Length;

    private readonly List<MigrationSourceFile> _files = [];
    private readonly List<MigrationRowError> _errors = [];

    private MigrationRun()
    {
        Source = string.Empty;
        MappingSha256 = string.Empty;
    }

    /// <summary>Gets when the run started.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Gets when the run finished.</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>Gets a value indicating whether the run rolled its changes back by design.</summary>
    public bool IsDryRun { get; private set; }

    /// <summary>Gets the source folder as the operator named it.</summary>
    public string Source { get; private set; }

    /// <summary>Gets the lowercase hex SHA-256 of the mapping file.</summary>
    public string MappingSha256 { get; private set; }

    /// <summary>Gets how the run ended, once finished.</summary>
    public MigrationOutcome? Outcome { get; private set; }

    /// <summary>Gets the number of data rows read, blank rows included.</summary>
    public int RowsRead { get; private set; }

    /// <summary>Gets the number of blank rows skipped.</summary>
    public int BlankRows { get; private set; }

    /// <summary>Gets the number of rows a later row for the same return replaced.</summary>
    public int SupersededRows { get; private set; }

    /// <summary>Gets the number of rows rejected.</summary>
    public int RejectedRows { get; private set; }

    /// <summary>Gets the number of returns migrated by this run (or that would have been, in a dry run).</summary>
    public int MigratedReturns { get; private set; }

    /// <summary>Gets the number of rows whose return an earlier run already migrated.</summary>
    public int AlreadyMigratedReturns { get; private set; }

    /// <summary>Gets the number of reconciliation differences.</summary>
    public int Mismatches { get; private set; }

    /// <summary>Gets the files read.</summary>
    public IReadOnlyCollection<MigrationSourceFile> Files => _files.AsReadOnly();

    /// <summary>Gets the rejected and superseded rows.</summary>
    public IReadOnlyCollection<MigrationRowError> Errors => _errors.AsReadOnly();

    /// <summary>Starts a run.</summary>
    /// <param name="source">The source folder as the operator named it.</param>
    /// <param name="mappingSha256">The SHA-256 of the mapping file.</param>
    /// <param name="isDryRun">Whether the run rolls its changes back.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The run.</returns>
    public static MigrationRun Start(string source, string mappingSha256, bool isDryRun, DateTimeOffset now) => new()
    {
        Source = Guard.NotBlank(source, SourceMaxLength),
        MappingSha256 = Sha256(mappingSha256),
        IsDryRun = isDryRun,
        StartedAt = now,
    };

    /// <summary>Records a file the run read.</summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="returnTypeCode">The return type the file holds.</param>
    /// <param name="sha256">The SHA-256 of the file's bytes.</param>
    /// <param name="rows">The number of data rows.</param>
    public void AddFile(string fileName, string returnTypeCode, string sha256, int rows)
    {
        EnsureOpen();
        _files.Add(new MigrationSourceFile
        {
            FileName = Guard.NotBlank(fileName, MigrationSourceFile.FileNameMaxLength),
            ReturnTypeCode = Guard.NotBlank(returnTypeCode, MigrationRowError.CodeMaxLength),
            Sha256 = Sha256(sha256),
            Rows = rows >= 0 ? rows : throw new DomainException("A file cannot have a negative number of rows."),
        });
    }

    /// <summary>Records a rejected or superseded row.</summary>
    /// <param name="error">The error entry.</param>
    public void AddError(MigrationRowError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        EnsureOpen();
        _errors.Add(error);
    }

    /// <summary>Finishes the run with its row accounting and reconciliation result.</summary>
    /// <param name="totals">How the source rows were accounted for.</param>
    /// <param name="mismatches">The number of reconciliation differences.</param>
    /// <param name="now">The current time.</param>
    public void Finish(MigrationTotals totals, int mismatches, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(totals);
        EnsureOpen();
        if (mismatches < 0)
        {
            throw new DomainException("Mismatches cannot be negative.");
        }

        RowsRead = totals.RowsRead;
        BlankRows = totals.BlankRows;
        SupersededRows = totals.SupersededRows;
        RejectedRows = totals.RejectedRows;
        MigratedReturns = totals.MigratedReturns;
        AlreadyMigratedReturns = totals.AlreadyMigratedReturns;
        Mismatches = mismatches;
        Outcome = mismatches == 0 && totals.IsBalanced ? MigrationOutcome.Reconciled : MigrationOutcome.Mismatch;
        FinishedAt = now;
    }

    private static string Sha256(string? value) =>
        value is { Length: Sha256Length } && value.All(char.IsAsciiHexDigitLower)
            ? value
            : throw new DomainException("A SHA-256 digest is 64 lowercase hex characters.");

    private void EnsureOpen()
    {
        if (Outcome is not null)
        {
            throw new DomainException("The migration run has finished.");
        }
    }
}

/// <summary>How a run accounted for its source rows: every row read is blank, superseded, rejected or a return.</summary>
/// <param name="RowsRead">Data rows read, blank rows included.</param>
/// <param name="BlankRows">Blank rows skipped.</param>
/// <param name="SupersededRows">Rows a later row for the same return replaced.</param>
/// <param name="RejectedRows">Rows rejected.</param>
/// <param name="MigratedReturns">Returns migrated by the run.</param>
/// <param name="AlreadyMigratedReturns">Rows whose return an earlier run migrated.</param>
public sealed record MigrationTotals(
    int RowsRead, int BlankRows, int SupersededRows, int RejectedRows, int MigratedReturns, int AlreadyMigratedReturns)
{
    /// <summary>Gets a value indicating whether every row read is accounted for exactly once.</summary>
    public bool IsBalanced =>
        RowsRead == BlankRows + SupersededRows + RejectedRows + MigratedReturns + AlreadyMigratedReturns;
}
