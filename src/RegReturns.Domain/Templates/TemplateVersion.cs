using RegReturns.Domain.Common;
using RegReturns.Domain.Periods;

namespace RegReturns.Domain.Templates;

/// <summary>
/// A versioned definition of the fields and rules for a return type.
/// Submissions pin the version they were captured with, so changing a template never rewrites history:
/// only drafts can change, and a change to a published template is a new version (ADR 0009).
/// </summary>
public sealed class TemplateVersion : Entity
{
    private readonly List<TemplateField> _fields = [];
    private readonly List<ValidationRule> _rules = [];

    private TemplateVersion()
    {
    }

    /// <summary>Gets the return type id.</summary>
    public Guid ReturnTypeId { get; private set; }

    /// <summary>Gets the version number, starting at 1.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// Gets the first reporting-period start date the version applies to. A period uses the published version with the
    /// latest <see cref="EffectiveFrom"/> on or before its start (<see cref="SelectFor"/>).
    /// </summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>Gets the lifecycle status.</summary>
    public TemplateStatus Status { get; private set; }

    /// <summary>Gets the fields. Use <see cref="TemplateField.DisplayOrder"/> to sort for display.</summary>
    public IReadOnlyCollection<TemplateField> Fields => _fields.AsReadOnly();

    /// <summary>Gets the validation rules.</summary>
    public IReadOnlyList<ValidationRule> Rules => _rules.AsReadOnly();

    /// <summary>Creates a draft template version.</summary>
    /// <param name="returnTypeId">The return type.</param>
    /// <param name="version">Version number (1 or more).</param>
    /// <param name="effectiveFrom">First reporting-period start date the version applies to.</param>
    /// <returns>The draft version.</returns>
    public static TemplateVersion CreateDraft(Guid returnTypeId, int version, DateOnly effectiveFrom) =>
        version < 1
            ? throw new DomainException("Version must be 1 or more.")
            : new TemplateVersion
            {
                ReturnTypeId = Guard.NotEmpty(returnTypeId),
                Version = version,
                EffectiveFrom = effectiveFrom,
                Status = TemplateStatus.Draft,
            };

    /// <summary>
    /// Returns the version a reporting period files against: the published version with the latest
    /// <see cref="EffectiveFrom"/> on or before the period's start, the higher version number winning a tie.
    /// Retired versions are never chosen; older published versions keep applying to the periods before a newer one.
    /// </summary>
    /// <param name="versions">The return type's versions.</param>
    /// <param name="period">The reporting period.</param>
    /// <returns>The version, or <see langword="null"/> if none applies.</returns>
    public static TemplateVersion? SelectFor(IEnumerable<TemplateVersion> versions, ReportingPeriod period)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(period);
        return versions
            .Where(v => v.Status == TemplateStatus.Published && v.EffectiveFrom <= period.Start)
            .OrderByDescending(v => v.EffectiveFrom)
            .ThenByDescending(v => v.Version)
            .FirstOrDefault();
    }

    /// <summary>Copies the fields and rules into a new draft version, the usual way to change a published template.</summary>
    /// <param name="version">The new version number; the caller picks the next free one.</param>
    /// <param name="effectiveFrom">First reporting-period start date the new version applies to.</param>
    /// <returns>The new draft.</returns>
    public TemplateVersion CopyAsDraft(int version, DateOnly effectiveFrom)
    {
        var draft = CreateDraft(ReturnTypeId, version, effectiveFrom);
        draft._fields.AddRange(_fields.OrderBy(f => f.DisplayOrder).Select(f => f.Copy()));
        draft._rules.AddRange(_rules.Select(r => r.Copy()));
        return draft;
    }

    /// <summary>Returns the field with the given code, if any.</summary>
    /// <param name="code">The field code.</param>
    /// <returns>The field, or <see langword="null"/>.</returns>
    public TemplateField? FindField(string code) =>
        _fields.Find(f => string.Equals(f.Code, code, StringComparison.Ordinal));

    /// <summary>Returns the rule with the given code, if any.</summary>
    /// <param name="code">The rule code.</param>
    /// <returns>The rule, or <see langword="null"/>.</returns>
    public ValidationRule? FindRule(string code) =>
        _rules.Find(r => string.Equals(r.Code, code, StringComparison.Ordinal));

    /// <summary>Adds a field to a draft template.</summary>
    /// <param name="code">Field code.</param>
    /// <param name="label">Label.</param>
    /// <param name="section">Section heading.</param>
    /// <param name="dataType">Data type.</param>
    /// <param name="unit">Display unit.</param>
    /// <param name="precision">Maximum decimal places.</param>
    /// <returns>The new field, or the rule that was broken.</returns>
    public Result<TemplateField> AddField(
        string code, string label, string section, FieldDataType dataType, string unit = "", int precision = 2) =>
        AddField(new FieldDefinition(code, label, section, dataType, unit, precision));

    /// <summary>Adds a field to the end of a draft template.</summary>
    /// <param name="definition">The field.</param>
    /// <returns>The new field, or the rule that was broken.</returns>
    public Result<TemplateField> AddField(FieldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (Status != TemplateStatus.Draft)
        {
            return TemplateErrors.NotDraft;
        }

        if (TemplateField.Check(definition) is { } invalid)
        {
            return invalid;
        }

        if (FindField(definition.Code.Trim()) is not null)
        {
            return TemplateErrors.DuplicateFieldCode;
        }

        var field = TemplateField.Create(definition, _fields.Count + 1);
        _fields.Add(field);
        return field;
    }

    /// <summary>
    /// Changes a field's label, section, data type, unit or precision; <see cref="FieldDefinition.Code"/> names the field
    /// and cannot change. A field cannot stop being numeric while range, cross-field or variance rules use it.
    /// </summary>
    /// <param name="definition">The field's new definition.</param>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result UpdateField(FieldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (Status != TemplateStatus.Draft)
        {
            return TemplateErrors.NotDraft;
        }

        if (TemplateField.Check(definition) is { } invalid)
        {
            return invalid;
        }

        var field = FindField(definition.Code.Trim());
        if (field is null)
        {
            return TemplateErrors.UnknownField.WithMessage($"Field '{definition.Code.Trim()}' is not in the template.");
        }

        if (field.IsNumeric && !TemplateField.IsNumericType(definition.DataType)
            && _rules.Exists(r => NeedsNumbers(r) && r.GetReferencedFieldCodes().Contains(field.Code)))
        {
            return TemplateErrors.FieldInUse.WithMessage(
                $"Range, cross-field or variance rules use {field.Code}, so it must stay a number. Change those rules first.");
        }

        field.Update(definition);
        return Result.Success();
    }

    /// <summary>Removes a field that no rule refers to, closing the gap in the display order.</summary>
    /// <param name="code">The field code.</param>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result RemoveField(string code)
    {
        if (Status != TemplateStatus.Draft)
        {
            return TemplateErrors.NotDraft;
        }

        var field = FindField(code);
        if (field is null)
        {
            return TemplateErrors.UnknownField.WithMessage($"Field '{code}' is not in the template.");
        }

        var users = _rules.Where(r => r.GetReferencedFieldCodes().Contains(field.Code)).Select(r => r.Code).ToList();
        if (users.Count > 0)
        {
            return TemplateErrors.FieldInUse.WithMessage(
                $"Rules {string.Join(", ", users)} refer to {field.Code}. Remove or change those rules first.");
        }

        _fields.Remove(field);
        Renumber(_fields.OrderBy(f => f.DisplayOrder).ToList());
        return Result.Success();
    }

    /// <summary>Moves a field up (negative offset) or down (positive offset) in the display order.</summary>
    /// <param name="code">The field code.</param>
    /// <param name="offset">Positions to move; moving past either end stops there.</param>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result MoveField(string code, int offset)
    {
        if (Status != TemplateStatus.Draft)
        {
            return TemplateErrors.NotDraft;
        }

        var ordered = _fields.OrderBy(f => f.DisplayOrder).ToList();
        var index = ordered.FindIndex(f => string.Equals(f.Code, code, StringComparison.Ordinal));
        if (index < 0)
        {
            return TemplateErrors.UnknownField.WithMessage($"Field '{code}' is not in the template.");
        }

        var field = ordered[index];
        ordered.RemoveAt(index);
        ordered.Insert(Math.Clamp(index + offset, 0, ordered.Count), field);
        Renumber(ordered);
        return Result.Success();
    }

    /// <summary>
    /// Adds a validation rule to a draft template. The target and every field a cross-field expression names must exist,
    /// and range, cross-field and variance rules only work on numeric fields.
    /// </summary>
    /// <param name="rule">The rule, created with <see cref="ValidationRule.Create"/> or one of its shortcuts.</param>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result AddRule(ValidationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (Status != TemplateStatus.Draft)
        {
            return TemplateErrors.NotDraft;
        }

        if (CheckRuleFields(rule) is { } invalid)
        {
            return invalid;
        }

        if (FindRule(rule.Code) is not null)
        {
            return TemplateErrors.DuplicateRuleCode;
        }

        _rules.Add(rule);
        return Result.Success();
    }

    /// <summary>Removes a rule from a draft template.</summary>
    /// <param name="code">The rule code.</param>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result RemoveRule(string code)
    {
        if (Status != TemplateStatus.Draft)
        {
            return TemplateErrors.NotDraft;
        }

        var rule = FindRule(code);
        if (rule is null)
        {
            return TemplateErrors.UnknownRule;
        }

        _rules.Remove(rule);
        return Result.Success();
    }

    /// <summary>Switches a rule on or off in a draft template; inactive rules are kept but not evaluated.</summary>
    /// <param name="code">The rule code.</param>
    /// <param name="active">Whether the rule is evaluated.</param>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result SetRuleActive(string code, bool active)
    {
        if (Status != TemplateStatus.Draft)
        {
            return TemplateErrors.NotDraft;
        }

        var rule = FindRule(code);
        if (rule is null)
        {
            return TemplateErrors.UnknownRule;
        }

        rule.SetActive(active);
        return Result.Success();
    }

    /// <summary>Changes the first reporting-period start date a draft applies to.</summary>
    /// <param name="effectiveFrom">The new date.</param>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result ChangeEffectiveFrom(DateOnly effectiveFrom)
    {
        if (Status != TemplateStatus.Draft)
        {
            return TemplateErrors.NotDraft;
        }

        EffectiveFrom = effectiveFrom;
        return Result.Success();
    }

    /// <summary>Publishes the template so banks can file against it. Published templates are immutable.</summary>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result Publish()
    {
        if (Status != TemplateStatus.Draft)
        {
            return TemplateErrors.NotDraft;
        }

        if (_fields.Count == 0)
        {
            return TemplateErrors.NoFields;
        }

        // Every edit keeps rules consistent with fields; checking again costs little and guards the invariant.
        foreach (var rule in _rules)
        {
            if (CheckRuleFields(rule) is { } invalid)
            {
                return invalid.WithMessage($"{rule.Code}: {invalid.Message}");
            }
        }

        Status = TemplateStatus.Published;
        return Result.Success();
    }

    /// <summary>
    /// Retires a published version so no new draft files against it. Returns already captured with it keep it.
    /// </summary>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result Retire()
    {
        if (Status != TemplateStatus.Published)
        {
            return TemplateErrors.NotPublished;
        }

        Status = TemplateStatus.Retired;
        return Result.Success();
    }

    private static bool NeedsNumbers(ValidationRule rule) =>
        rule.RuleType is RuleType.Range or RuleType.CrossField or RuleType.Variance;

    private static void Renumber(List<TemplateField> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].MoveTo(i + 1);
        }
    }

    private Error? CheckRuleFields(ValidationRule rule)
    {
        foreach (var code in rule.GetReferencedFieldCodes().Order(StringComparer.Ordinal))
        {
            var field = FindField(code);
            if (field is null)
            {
                return TemplateErrors.UnknownField.WithMessage($"Field '{code}' is not in the template.");
            }

            if (NeedsNumbers(rule) && !field.IsNumeric)
            {
                return TemplateErrors.RuleNeedsNumericField.WithMessage(
                    $"{field.Code} is a {field.DataType} field; {rule.RuleType} rules only work on numeric fields.");
            }
        }

        return null;
    }
}
