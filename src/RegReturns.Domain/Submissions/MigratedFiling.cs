namespace RegReturns.Domain.Submissions;

/// <summary>What the legacy returns system recorded about a return it approved, and the notes a migration adds.</summary>
/// <param name="SubmittedAt">When the bank filed the return in the legacy system.</param>
/// <param name="ApprovedAt">When the regulator approved it there.</param>
/// <param name="Comment">The comment on the migration step, naming the migration run.</param>
/// <param name="WarningNote">The note kept with each warning, since the legacy system recorded no justifications.</param>
public sealed record MigratedFiling(DateTimeOffset SubmittedAt, DateTimeOffset ApprovedAt, string Comment, string WarningNote)
{
    /// <summary>
    /// Returns whether the dates are in order: filed on or after the period end, approved on or after filing, and
    /// approved no later than now.
    /// </summary>
    /// <param name="periodEnd">The last day of the reporting period.</param>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> if the dates are in order.</returns>
    public bool IsInOrder(DateOnly periodEnd, DateTimeOffset now) =>
        DateOnly.FromDateTime(SubmittedAt.UtcDateTime) >= periodEnd && ApprovedAt >= SubmittedAt && ApprovedAt <= now;
}
