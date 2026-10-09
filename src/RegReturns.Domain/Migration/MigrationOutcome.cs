namespace RegReturns.Domain.Migration;

/// <summary>How a legacy migration run ended.</summary>
public enum MigrationOutcome
{
    /// <summary>Every row is accounted for and every migrated figure matches its source.</summary>
    Reconciled = 1,

    /// <summary>Reconciliation found a difference, so nothing was committed.</summary>
    Mismatch = 2,
}

/// <summary>Why a legacy row did not become a migrated return.</summary>
public enum RowErrorKind
{
    /// <summary>The row was not migrated because it failed cleansing, mapping or validation.</summary>
    Rejected = 1,

    /// <summary>A later row for the same bank, return type and period replaced it (last row wins).</summary>
    Superseded = 2,
}
