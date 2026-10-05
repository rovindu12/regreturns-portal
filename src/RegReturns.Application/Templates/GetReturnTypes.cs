using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Templates;

/// <summary>Asks for the return types banks file today, with their published template versions (reference data).</summary>
[SuppressMessage(
    "Major Code Smell",
    "S2094:Classes should not be empty",
    Justification = "A query without inputs: the handler interface needs a type to dispatch on.")]
public sealed record GetReturnTypes;

/// <summary>A return type banks file.</summary>
/// <param name="Code">The code, such as <c>MLR</c>.</param>
/// <param name="Name">The name.</param>
/// <param name="Description">What the return covers.</param>
/// <param name="Frequency">How often it is filed.</param>
/// <param name="DueDaysAfterPeriodEnd">How many days after the period ends it is due.</param>
/// <param name="Versions">The published template versions, newest first.</param>
public sealed record ReturnTypeInfo(
    string Code,
    string Name,
    string Description,
    ReturnFrequency Frequency,
    int DueDaysAfterPeriodEnd,
    IReadOnlyList<PublishedTemplateVersion> Versions);

/// <summary>A published template version.</summary>
/// <param name="Version">The version number.</param>
/// <param name="EffectiveFrom">The first reporting-period start it applies to.</param>
public sealed record PublishedTemplateVersion(int Version, DateOnly EffectiveFrom);

/// <summary>Handles <see cref="GetReturnTypes"/>.</summary>
/// <param name="db">The unit of work.</param>
public sealed class GetReturnTypesHandler(IAppDbContext db) : IQueryHandler<GetReturnTypes, IReadOnlyList<ReturnTypeInfo>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ReturnTypeInfo>> HandleAsync(GetReturnTypes query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var returnTypes = await db.ReturnTypes.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Code)
            .ToListAsync(cancellationToken);
        var ids = returnTypes.Select(r => r.Id).ToList();
        var versions = await db.TemplateVersions.AsNoTracking()
            .Where(v => ids.Contains(v.ReturnTypeId) && v.Status == TemplateStatus.Published)
            .Select(v => new { v.ReturnTypeId, v.Version, v.EffectiveFrom })
            .ToListAsync(cancellationToken);

        return [.. returnTypes.Select(r => new ReturnTypeInfo(
            r.Code,
            r.Name,
            r.Description,
            r.Frequency,
            r.DueDaysAfterPeriodEnd,
            [.. versions.Where(v => v.ReturnTypeId == r.Id)
                .OrderByDescending(v => v.Version)
                .Select(v => new PublishedTemplateVersion(v.Version, v.EffectiveFrom))]))];
    }
}

/// <summary>
/// Asks for the template a return is filed with: the published version that applies to a reporting period (ADR 0009),
/// or to the current period when none is named.
/// </summary>
/// <param name="ReturnTypeCode">The return type code (case-insensitive).</param>
/// <param name="Period">The reporting period, or <see langword="null"/> for the current one.</param>
public sealed record GetReturnTypeTemplate(string ReturnTypeCode, ReportingPeriod? Period = null);

/// <summary>A template version as a bank system needs it to fill in a return.</summary>
/// <param name="ReturnTypeCode">The return type code.</param>
/// <param name="Version">The version number.</param>
/// <param name="EffectiveFrom">The first reporting-period start it applies to.</param>
/// <param name="PeriodLabel">The period it was selected for.</param>
/// <param name="Fields">The fields, in display order.</param>
/// <param name="Rules">The active validation rules, by field order and then code.</param>
public sealed record ReturnTypeTemplate(
    string ReturnTypeCode,
    int Version,
    DateOnly EffectiveFrom,
    string PeriodLabel,
    IReadOnlyList<TemplateFieldInfo> Fields,
    IReadOnlyList<TemplateRuleInfo> Rules);

/// <summary>A field of a template.</summary>
/// <param name="Code">The field code values are keyed by.</param>
/// <param name="Label">The label.</param>
/// <param name="Section">The section.</param>
/// <param name="DataType">The data type.</param>
/// <param name="Unit">The unit, such as <c>VLD</c> or <c>%</c>.</param>
/// <param name="Precision">The most decimal places a number may have.</param>
/// <param name="IsRequired">Whether an active rule requires a value.</param>
public sealed record TemplateFieldInfo(
    string Code, string Label, string Section, FieldDataType DataType, string Unit, int Precision, bool IsRequired);

/// <summary>An active validation rule of a template.</summary>
/// <param name="Code">The rule code findings carry.</param>
/// <param name="RuleType">What kind of check it is.</param>
/// <param name="Severity">Whether a finding blocks submission (error) or needs a justification (warning).</param>
/// <param name="FieldCode">The field findings are reported against.</param>
/// <param name="Message">The message findings carry.</param>
public sealed record TemplateRuleInfo(string Code, RuleType RuleType, Severity Severity, string FieldCode, string Message);

/// <summary>Errors of the return type queries.</summary>
public static class ReturnTypeErrors
{
    /// <summary>No active return type has the code.</summary>
    public static readonly Error NotFound = new("ReturnType.NotFound", "There is no return type with this code.");

    /// <summary>The period's frequency is not the return type's.</summary>
    public static readonly Error PeriodMismatch = new(
        "ReturnType.PeriodMismatch", "The period does not match how often this return is filed.");

    /// <summary>Returns <see cref="PeriodMismatch"/> with an example of a valid period.</summary>
    /// <param name="code">The return type code.</param>
    /// <param name="frequency">How often it is filed.</param>
    /// <returns>The error.</returns>
    public static Error PeriodMismatchFor(string code, ReturnFrequency frequency) => PeriodMismatch.WithMessage(
        frequency == ReturnFrequency.Monthly
            ? $"{code} is filed monthly: name a month such as 2027-03."
            : $"{code} is filed quarterly: name a quarter such as 2027-Q1.");
}

/// <summary>Handles <see cref="GetReturnTypeTemplate"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="timeProvider">The clock, for the current period.</param>
public sealed class GetReturnTypeTemplateHandler(IAppDbContext db, TimeProvider timeProvider)
    : IQueryHandler<GetReturnTypeTemplate, Result<ReturnTypeTemplate>>
{
    /// <inheritdoc />
    public async Task<Result<ReturnTypeTemplate>> HandleAsync(GetReturnTypeTemplate query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var code = query.ReturnTypeCode.Trim().ToUpperInvariant();
        var returnType = await db.ReturnTypes.AsNoTracking().SingleOrDefaultAsync(r => r.Code == code && r.IsActive, cancellationToken);
        if (returnType is null)
        {
            return ReturnTypeErrors.NotFound;
        }

        var period = query.Period
            ?? ReportingPeriod.Containing(returnType.Frequency, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
        if (period.Frequency != returnType.Frequency)
        {
            return ReturnTypeErrors.PeriodMismatchFor(returnType.Code, returnType.Frequency);
        }

        var versions = await db.TemplateVersions.AsNoTracking()
            .Include(v => v.Fields)
            .Include(v => v.Rules)
            .Where(v => v.ReturnTypeId == returnType.Id && v.Status == TemplateStatus.Published)
            .ToListAsync(cancellationToken);
        var template = TemplateVersion.SelectFor(versions, period);
        if (template is null)
        {
            return TemplateErrors.NoApplicableVersion;
        }

        var rules = template.Rules.Where(r => r.IsActive).ToList();
        var fields = template.Fields.OrderBy(f => f.DisplayOrder).ToList();
        var order = fields.Select((f, i) => (f.Code, i)).ToDictionary(x => x.Code, x => x.i, StringComparer.Ordinal);
        return new ReturnTypeTemplate(
            returnType.Code,
            template.Version,
            template.EffectiveFrom,
            period.Label,
            [.. fields.Select(f => new TemplateFieldInfo(
                f.Code, f.Label, f.Section, f.DataType, f.Unit, f.Precision,
                rules.Exists(r => r.RuleType == RuleType.Required && r.TargetFieldCode == f.Code)))],
            [.. rules
                .OrderBy(r => order.GetValueOrDefault(r.TargetFieldCode, int.MaxValue))
                .ThenBy(r => r.Code, StringComparer.Ordinal)
                .Select(r => new TemplateRuleInfo(r.Code, r.RuleType, r.Severity, r.TargetFieldCode, r.Message))]);
    }
}
