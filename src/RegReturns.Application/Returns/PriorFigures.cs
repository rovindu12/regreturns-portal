using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Returns;

/// <summary>
/// The bank's approved figures for the periods a return is compared with: the previous period and the same period last
/// year. Variance rules (plan §5) and advisory insights (ADR 0030) both read them, so both compare against the same
/// figures.
/// </summary>
internal static class PriorFigures
{
    /// <summary>Loads the numeric values of the last approved return for each comparison basis.</summary>
    /// <param name="db">The unit of work.</param>
    /// <param name="submission">The return being compared.</param>
    /// <param name="period">Its reporting period.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Figures keyed by field code, per basis; a basis is missing when that period has no approved return.</returns>
    public static async Task<IReadOnlyDictionary<VarianceBasis, IReadOnlyDictionary<string, decimal>>> ForAsync(
        IAppDbContext db, Submission submission, ReportingPeriod period, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(period);
        var prior = new Dictionary<VarianceBasis, IReadOnlyDictionary<string, decimal>>();
        foreach (var (basis, priorPeriod) in new[]
        {
            (VarianceBasis.PreviousPeriod, period.Previous()),
            (VarianceBasis.SamePeriodLastYear, period.SamePeriodLastYear()),
        })
        {
            if (await ApprovedFiguresAsync(db, submission, priorPeriod, cancellationToken) is { } figures)
            {
                prior[basis] = figures;
            }
        }

        return prior;
    }

    private static async Task<IReadOnlyDictionary<string, decimal>?> ApprovedFiguresAsync(
        IAppDbContext db, Submission submission, ReportingPeriod period, CancellationToken cancellationToken)
    {
        var figures = await db.Submissions.AsNoTracking()
            .Where(s => s.InstitutionId == submission.InstitutionId
                && s.ReturnTypeId == submission.ReturnTypeId
                && s.Status == SubmissionStatus.Approved
                && db.Obligations.Any(o => o.Id == s.ObligationId
                    && o.Period.Frequency == period.Frequency
                    && o.Period.Year == period.Year
                    && o.Period.Number == period.Number))
            .OrderByDescending(s => s.DecidedAt)
            .Select(s => s.Values.Where(v => v.NumericValue != null).Select(v => new { v.FieldCode, v.NumericValue }).ToList())
            .FirstOrDefaultAsync(cancellationToken);
        return figures?.ToDictionary(v => v.FieldCode, v => v.NumericValue!.Value, StringComparer.Ordinal);
    }
}
