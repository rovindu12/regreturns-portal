namespace RegReturns.Application.Reporting;

/// <summary>
/// The read model of a host that draws no reports (the API): every read refuses. <c>AddReporting</c> in Infrastructure
/// replaces it, so only a host wiring a report handler by mistake ever reaches it.
/// </summary>
public sealed class UnavailableReportingReadModel : IReportingReadModel
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ObligationRow>> GetObligationsAsync(ObligationQuery filter, CancellationToken cancellationToken) =>
        throw Unavailable();

    /// <inheritdoc />
    public Task<IReadOnlyList<FindingCountRow>> GetFindingCountsAsync(FindingQuery filter, CancellationToken cancellationToken) =>
        throw Unavailable();

    /// <inheritdoc />
    public Task<IReadOnlyList<ApprovedValueRow>> GetApprovedValuesAsync(ApprovedValueQuery filter, CancellationToken cancellationToken) =>
        throw Unavailable();

    private static InvalidOperationException Unavailable() =>
        new("This host draws no reports: AddReporting (Infrastructure) registers the reporting read model.");
}

/// <summary>The renderer of a host that draws no reports (the API): rendering refuses. <c>AddReporting</c> replaces it.</summary>
public sealed class UnavailableComplianceReportRenderer : IComplianceReportRenderer
{
    /// <inheritdoc />
    public byte[] Render(ComplianceReportDocument document, ReportFormat format) =>
        throw new InvalidOperationException("This host draws no reports: AddReporting (Infrastructure) registers the renderer.");
}
