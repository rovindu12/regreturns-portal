using System.Globalization;

using RegReturns.Application.Auditing;
using RegReturns.Domain.Common;

namespace RegReturns.Application.Demo;

/// <summary>
/// Replaces the demo workload with the seed in one transaction and records a <c>DemoReset</c> audit event in it
/// (ADR 0031). Implemented in Infrastructure.
/// </summary>
public interface IDemoReset
{
    /// <summary>Runs the reset, unless another one is running, the cooldown has not passed, or the database is not a demo.</summary>
    /// <param name="request">What started the reset and who asked.</param>
    /// <param name="cancellationToken">Cancels the reset; nothing is changed then.</param>
    /// <returns>What was removed and seeded, or <see cref="DemoErrors.InProgress"/>, <see cref="DemoErrors.CoolingDown"/> or <see cref="DemoErrors.NotADemoDatabase"/>.</returns>
    Task<Result<DemoResetReport>> ResetAsync(DemoResetRequest request, CancellationToken cancellationToken);
}

/// <summary>What started a demo reset.</summary>
public enum DemoResetTrigger
{
    /// <summary>The nightly job (<c>Demo:ResetSchedule</c>), acting as the system.</summary>
    Scheduled = 1,

    /// <summary>A system administrator pressed <em>Reset demo</em>.</summary>
    Manual = 2,
}

/// <summary>A request to reset the demo.</summary>
/// <param name="Trigger">What started it.</param>
/// <param name="Origin">Who asked and from where, recorded in the audit event.</param>
/// <param name="Cooldown">How long after the latest reset a new one is refused, or <see langword="null"/> for none.</param>
public sealed record DemoResetRequest(DemoResetTrigger Trigger, AuditOrigin Origin, TimeSpan? Cooldown);

/// <summary>The rows one table lost in a reset.</summary>
/// <param name="Table">The table, as <c>schema.Table</c>.</param>
/// <param name="Rows">Rows deleted.</param>
public sealed record DemoTableCount(string Table, int Rows);

/// <summary>What a reset seeded.</summary>
/// <param name="ReturnTypes">Return types.</param>
/// <param name="Templates">Template versions.</param>
/// <param name="Obligations">Return obligations.</param>
/// <param name="Submissions">Submissions (returns).</param>
/// <param name="Institutions">Institutions that were missing and were created.</param>
/// <param name="Users">Demo accounts that were missing and were created.</param>
public sealed record DemoSeedCounts(int ReturnTypes, int Templates, int Obligations, int Submissions, int Institutions, int Users);

/// <summary>The outcome of a demo reset.</summary>
/// <param name="Trigger">What started it.</param>
/// <param name="CompletedAt">When it committed.</param>
/// <param name="ElapsedMs">How long it took, in milliseconds.</param>
/// <param name="AuditSequence">The sequence number of its <c>DemoReset</c> audit entry.</param>
/// <param name="Removed">Rows deleted per table, in the order they were deleted.</param>
/// <param name="Seeded">What was seeded.</param>
public sealed record DemoResetReport(
    DemoResetTrigger Trigger,
    DateTimeOffset CompletedAt,
    long ElapsedMs,
    long AuditSequence,
    IReadOnlyList<DemoTableCount> Removed,
    DemoSeedCounts Seeded)
{
    /// <summary>Gets the total number of rows deleted.</summary>
    public int RowsRemoved => Removed.Sum(r => r.Rows);

    /// <summary>
    /// Describes a reset for its audit entry: the trigger, the rows removed in total and for returns, obligations and
    /// templates, what was seeded, and the duration. Never a figure from a return.
    /// </summary>
    /// <param name="trigger">What started it.</param>
    /// <param name="removed">Rows deleted per table.</param>
    /// <param name="seeded">What was seeded.</param>
    /// <param name="elapsedMs">How long it took before the entry was written.</param>
    /// <returns>The details text.</returns>
    public static string Describe(DemoResetTrigger trigger, IReadOnlyList<DemoTableCount> removed, DemoSeedCounts seeded, long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(seeded);
        int Rows(string table) => removed.Where(r => r.Table.EndsWith("." + table, StringComparison.Ordinal)).Sum(r => r.Rows);
        var created = seeded.Institutions + seeded.Users == 0
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $", {seeded.Institutions} missing institutions and {seeded.Users} missing demo accounts");
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(trigger == DemoResetTrigger.Scheduled ? "Scheduled" : "Manual")} demo reset; removed {removed.Sum(r => r.Rows)} rows ({Rows("Submissions")} returns, {Rows("Obligations")} obligations, {Rows("TemplateVersions")} template versions); seeded {seeded.Submissions} returns, {seeded.Obligations} obligations, {seeded.Templates} template versions{created}; directory and audit trail kept; {elapsedMs} ms");
    }
}
