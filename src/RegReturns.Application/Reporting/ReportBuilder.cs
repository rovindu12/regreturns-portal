using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Identity;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Reporting;

/// <summary>Errors raised by reports.</summary>
public static class ReportErrors
{
    /// <summary>The caller holds no role, so no report is theirs to see.</summary>
    public static readonly Error NotAllowed = new("Report.NotAllowed", "You do not have a role that can see reports.");

    /// <summary>No active return type exists to report on.</summary>
    public static readonly Error NoReturnTypes = new("Report.NoReturnTypes", "No return types are set up yet.");

    /// <summary>The requested return type does not exist or is not active.</summary>
    public static readonly Error UnknownReturnType = new("Report.UnknownReturnType", "That return type was not found.");
}

/// <summary>Who a report is for and what it covers.</summary>
/// <param name="Actor">The caller.</param>
/// <param name="Header">The return type, periods and day the report covers.</param>
public sealed record ReportScope(Actor Actor, ReportHeader Header)
{
    /// <summary>Gets the caller's bank, or <see langword="null"/> for regulator staff, who see every bank.</summary>
    public Guid? InstitutionId => Actor.InstitutionId;

    /// <summary>Gets the report's window of periods.</summary>
    public PeriodWindow Window => PeriodWindow.Of(Header.Periods);
}

/// <summary>
/// Resolves who a report is for and builds its parts from the reporting read model (ADR 0028). Bank staff see their own
/// bank only; regulator staff see every active bank, but a bank's draft only once it has been submitted.
/// </summary>
/// <param name="db">The unit of work, for reference data.</param>
/// <param name="currentActor">The caller.</param>
/// <param name="readModel">The reporting views.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="options">The report settings.</param>
public sealed class ReportBuilder(
    IAppDbContext db, ICurrentActor currentActor, IReportingReadModel readModel, TimeProvider timeProvider, IOptions<ReportingOptions> options)
{
    /// <summary>Resolves the caller, the return type (the first by code when none is asked for) and the periods.</summary>
    /// <param name="returnTypeCode">The return type's code, or <see langword="null"/> for the first.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The scope, or why there is none.</returns>
    public async Task<Result<ReportScope>> ResolveAsync(string? returnTypeCode, CancellationToken cancellationToken)
    {
        var actorResult = await currentActor.GetAsync(cancellationToken);
        if (actorResult.IsFailure)
        {
            return actorResult.Error!;
        }

        var actor = actorResult.Value;
        if (actor.Roles.Count == 0)
        {
            return ReportErrors.NotAllowed;
        }

        var returnTypes = await db.ReturnTypes.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Code)
            .Select(r => new ReportReturnType(r.Id, r.Code, r.Name, r.Frequency))
            .ToListAsync(cancellationToken);
        if (returnTypes.Count == 0)
        {
            return ReportErrors.NoReturnTypes;
        }

        var returnType = string.IsNullOrWhiteSpace(returnTypeCode)
            ? returnTypes[0]
            : returnTypes.Find(r => string.Equals(r.Code, returnTypeCode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (returnType is null)
        {
            return ReportErrors.UnknownReturnType;
        }

        var institution = actor.InstitutionId is { } institutionId
            ? await db.Institutions.AsNoTracking()
                .Where(i => i.Id == institutionId)
                .Select(i => new ReportInstitution(i.Code, i.Name))
                .SingleAsync(cancellationToken)
            : null;
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var periods = Compliance.Window(returnType.Frequency, today, options.Value.PeriodsShown(returnType.Frequency));
        return new ReportScope(actor, new ReportHeader(returnType, returnTypes, periods, today, institution));
    }

    /// <summary>Builds the compliance grid of the scope's return type and the overdue list of every return type.</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The compliance report.</returns>
    public async Task<ComplianceReport> ComplianceAsync(ReportScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var institutionId = scope.InstitutionId;
        var institutions = await db.Institutions.AsNoTracking()
            .Where(i => i.IsActive && (institutionId == null || i.Id == institutionId))
            .Select(i => new ReportInstitution(i.Code, i.Name))
            .ToListAsync(cancellationToken);
        var obligations = await readModel.GetObligationsAsync(
            new ObligationQuery(institutionId, scope.Header.ReturnType.Code, scope.Window), cancellationToken);
        var overdue = await readModel.GetObligationsAsync(
            new ObligationQuery(institutionId, null, OpenDueBefore: scope.Header.AsOf), cancellationToken);
        return ReportShaping.Compliance(institutions, scope.Header.Periods, obligations, overdue, scope.Header.AsOf, scope.Actor.IsRegulatorStaff);
    }

    /// <summary>Builds the trend of validation findings of submitted returns.</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The findings trend.</returns>
    public async Task<FindingTrend> FindingsAsync(ReportScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var counts = await readModel.GetFindingCountsAsync(
            new FindingQuery(scope.InstitutionId, scope.Header.ReturnType.Code, scope.Window), cancellationToken);
        return ReportShaping.Findings(scope.Header.Periods, counts);
    }

    /// <summary>
    /// Builds the trends of the key ratios configured for the scope's return type (<c>Reports:KeyRatios</c>), labelled
    /// as in the latest published template that has the field. A configured field no template has is skipped.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One trend per key ratio, in configuration order.</returns>
    public async Task<IReadOnlyList<KeyRatioTrend>> KeyRatiosAsync(ReportScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var returnType = scope.Header.ReturnType;
        var codes = options.Value.KeyRatios
            .Where(k => string.Equals(k.ReturnType, returnType.Code, StringComparison.OrdinalIgnoreCase))
            .Select(k => k.Field)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (codes.Count == 0)
        {
            return [];
        }

        var fields = await db.TemplateVersions.AsNoTracking()
            .Where(v => v.ReturnTypeId == returnType.Id && v.Status != TemplateStatus.Draft)
            .SelectMany(v => v.Fields
                .Where(f => codes.Contains(f.Code))
                .Select(f => new { v.Version, f.Code, f.Label, f.Unit, f.Precision }))
            .ToListAsync(cancellationToken);
        var latest = fields
            .GroupBy(f => f.Code, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.MaxBy(f => f.Version)!, StringComparer.Ordinal);
        var known = codes.Where(latest.ContainsKey).ToList();
        if (known.Count == 0)
        {
            return [];
        }

        var values = await readModel.GetApprovedValuesAsync(
            new ApprovedValueQuery(scope.InstitutionId, returnType.Code, known, scope.Window), cancellationToken);
        return [.. known.Select(code =>
        {
            var field = latest[code];
            return ReportShaping.KeyRatio(new KeyRatioField(code, field.Label, field.Unit, field.Precision), scope.Header.Periods, values);
        })];
    }
}
