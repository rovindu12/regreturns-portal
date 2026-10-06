using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;

namespace RegReturns.Application.Reporting;

/// <summary>
/// Asks for the reports dashboard of one return type: filing compliance, the overdue list, the trend of validation
/// findings and the key ratios. Bank staff see their own bank; regulator staff see every bank.
/// </summary>
/// <param name="ReturnTypeCode">The return type's code, or <see langword="null"/> for the first by code.</param>
public sealed record GetReportsDashboard(string? ReturnTypeCode = null);

/// <summary>Handles <see cref="GetReportsDashboard"/> for every portal role.</summary>
/// <param name="builder">Builds the report parts.</param>
public sealed class ReportsDashboardHandler(ReportBuilder builder) : IQueryHandler<GetReportsDashboard, Result<ReportsDashboard>>
{
    /// <inheritdoc />
    public async Task<Result<ReportsDashboard>> HandleAsync(GetReportsDashboard query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scopeResult = await builder.ResolveAsync(query.ReturnTypeCode, cancellationToken);
        if (scopeResult.IsFailure)
        {
            return scopeResult.Error!;
        }

        var scope = scopeResult.Value;
        return new ReportsDashboard(
            scope.Header,
            await builder.ComplianceAsync(scope, cancellationToken),
            await builder.FindingsAsync(scope, cancellationToken),
            await builder.KeyRatiosAsync(scope, cancellationToken));
    }
}
