using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Templates;

/// <summary>Asks for one template version with its fields and rules.</summary>
/// <param name="Id">The version id.</param>
public sealed record GetTemplateVersion(Guid Id);

/// <summary>A template version with everything the edit screen shows.</summary>
/// <param name="Id">The version id.</param>
/// <param name="ReturnTypeId">The return type id.</param>
/// <param name="ReturnTypeCode">The return type code.</param>
/// <param name="ReturnTypeName">The return type name.</param>
/// <param name="Version">The version number.</param>
/// <param name="Status">Draft, published or retired.</param>
/// <param name="EffectiveFrom">First reporting-period start date it applies to.</param>
/// <param name="Fields">The fields in display order.</param>
/// <param name="Rules">The rules, by target field display order, then rule type and code.</param>
/// <param name="SubmissionCount">Number of returns captured with this version.</param>
public sealed record TemplateVersionDetails(
    Guid Id,
    Guid ReturnTypeId,
    string ReturnTypeCode,
    string ReturnTypeName,
    int Version,
    TemplateStatus Status,
    DateOnly EffectiveFrom,
    IReadOnlyList<TemplateFieldDetails> Fields,
    IReadOnlyList<ValidationRuleDetails> Rules,
    int SubmissionCount)
{
    /// <summary>Gets a value indicating whether the version can still be edited.</summary>
    public bool IsDraft => Status == TemplateStatus.Draft;
}

/// <summary>A field of a template version.</summary>
/// <param name="Definition">The field definition.</param>
/// <param name="DisplayOrder">The display position, starting at 1.</param>
/// <param name="UsedByRules">The codes of the rules that read the field.</param>
public sealed record TemplateFieldDetails(FieldDefinition Definition, int DisplayOrder, IReadOnlyList<string> UsedByRules);

/// <summary>A validation rule of a template version.</summary>
/// <param name="Definition">The rule definition.</param>
/// <param name="IsActive">Whether the rule is evaluated.</param>
public sealed record ValidationRuleDetails(RuleDefinition Definition, bool IsActive);

/// <summary>Handles <see cref="GetTemplateVersion"/>.</summary>
/// <param name="db">The unit of work.</param>
public sealed class GetTemplateVersionHandler(IAppDbContext db) : IQueryHandler<GetTemplateVersion, TemplateVersionDetails?>
{
    /// <inheritdoc />
    public async Task<TemplateVersionDetails?> HandleAsync(GetTemplateVersion query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var version = await db.TemplateVersions.AsNoTracking()
            .Include(v => v.Fields)
            .Include(v => v.Rules)
            .SingleOrDefaultAsync(v => v.Id == query.Id, cancellationToken);
        if (version is null)
        {
            return null;
        }

        var returnType = await db.ReturnTypes.AsNoTracking()
            .Where(r => r.Id == version.ReturnTypeId)
            .Select(r => new { r.Code, r.Name })
            .SingleAsync(cancellationToken);
        var submissions = await db.Submissions.CountAsync(s => s.TemplateVersionId == version.Id, cancellationToken);

        var usage = version.Rules
            .SelectMany(r => r.GetReferencedFieldCodes().Select(code => (Field: code, Rule: r.Code)))
            .ToLookup(u => u.Field, u => u.Rule, StringComparer.Ordinal);
        var fields = version.Fields
            .OrderBy(f => f.DisplayOrder)
            .Select(f => new TemplateFieldDetails(f.ToDefinition(), f.DisplayOrder, [.. usage[f.Code]]))
            .ToList();
        var order = version.Fields.ToDictionary(f => f.Code, f => f.DisplayOrder, StringComparer.Ordinal);
        var rules = version.Rules
            .OrderBy(r => order[r.TargetFieldCode])
            .ThenBy(r => r.RuleType)
            .ThenBy(r => r.Code, StringComparer.Ordinal)
            .Select(r => new ValidationRuleDetails(r.ToDefinition(), r.IsActive))
            .ToList();

        return new TemplateVersionDetails(
            version.Id, version.ReturnTypeId, returnType.Code, returnType.Name, version.Version, version.Status,
            version.EffectiveFrom, fields, rules, submissions);
    }
}
