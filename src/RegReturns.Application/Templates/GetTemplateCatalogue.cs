using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Templates;

/// <summary>Asks for return types with their template versions, for the administration screens.</summary>
/// <param name="ReturnTypeCode">Only this return type, or <see langword="null"/> for all of them.</param>
public sealed record GetTemplateCatalogue(string? ReturnTypeCode = null);

/// <summary>A return type and its template versions, newest first.</summary>
/// <param name="ReturnTypeId">The return type id.</param>
/// <param name="Code">The return type code, such as <c>MLR</c>.</param>
/// <param name="Name">The return type name.</param>
/// <param name="Frequency">How often it is filed.</param>
/// <param name="IsActive">Whether banks currently file it.</param>
/// <param name="Versions">The template versions, newest first.</param>
public sealed record ReturnTypeTemplates(
    Guid ReturnTypeId, string Code, string Name, ReturnFrequency Frequency, bool IsActive, IReadOnlyList<TemplateVersionSummary> Versions)
{
    /// <summary>Gets the draft version, if one is being prepared.</summary>
    public TemplateVersionSummary? Draft => Versions.FirstOrDefault(v => v.Status == TemplateStatus.Draft);
}

/// <summary>One template version in the catalogue.</summary>
/// <param name="Id">The version id.</param>
/// <param name="Version">The version number.</param>
/// <param name="Status">Draft, published or retired.</param>
/// <param name="EffectiveFrom">First reporting-period start date it applies to.</param>
/// <param name="FieldCount">Number of fields.</param>
/// <param name="RuleCount">Number of rules (active or not).</param>
/// <param name="SubmissionCount">Number of returns captured with it.</param>
public sealed record TemplateVersionSummary(
    Guid Id, int Version, TemplateStatus Status, DateOnly EffectiveFrom, int FieldCount, int RuleCount, int SubmissionCount);

/// <summary>Handles <see cref="GetTemplateCatalogue"/>.</summary>
/// <param name="db">The unit of work.</param>
public sealed class GetTemplateCatalogueHandler(IAppDbContext db) : IQueryHandler<GetTemplateCatalogue, IReadOnlyList<ReturnTypeTemplates>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ReturnTypeTemplates>> HandleAsync(GetTemplateCatalogue query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var returnTypes = await db.ReturnTypes.AsNoTracking()
            .Where(r => query.ReturnTypeCode == null || r.Code == query.ReturnTypeCode)
            .OrderBy(r => r.Code)
            .Select(r => new { r.Id, r.Code, r.Name, r.Frequency, r.IsActive })
            .ToListAsync(cancellationToken);
        var ids = returnTypes.Select(r => r.Id).ToList();
        var versions = await db.TemplateVersions.AsNoTracking()
            .Where(v => ids.Contains(v.ReturnTypeId))
            .Select(v => new
            {
                v.ReturnTypeId,
                Summary = new TemplateVersionSummary(
                    v.Id, v.Version, v.Status, v.EffectiveFrom, v.Fields.Count, v.Rules.Count,
                    db.Submissions.Count(s => s.TemplateVersionId == v.Id)),
            })
            .ToListAsync(cancellationToken);

        return [.. returnTypes.Select(r => new ReturnTypeTemplates(
            r.Id, r.Code, r.Name, r.Frequency, r.IsActive,
            [.. versions.Where(v => v.ReturnTypeId == r.Id).Select(v => v.Summary).OrderByDescending(v => v.Version)]))];
    }
}
