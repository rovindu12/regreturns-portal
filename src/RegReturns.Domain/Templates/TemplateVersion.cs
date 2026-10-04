using RegReturns.Domain.Common;

namespace RegReturns.Domain.Templates;

/// <summary>
/// A versioned definition of the fields and rules for a return type.
/// Submissions pin the version they were captured with, so changing a template never rewrites history.
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

    /// <summary>Gets the first date from which the version may be used for filing.</summary>
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
    /// <param name="effectiveFrom">First date the version may be used.</param>
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

    /// <summary>Returns the field with the given code, if any.</summary>
    /// <param name="code">The field code.</param>
    /// <returns>The field, or <see langword="null"/>.</returns>
    public TemplateField? FindField(string code) =>
        _fields.Find(f => string.Equals(f.Code, code, StringComparison.Ordinal));

    /// <summary>Adds a field to a draft template.</summary>
    /// <param name="code">Field code.</param>
    /// <param name="label">Label.</param>
    /// <param name="section">Section heading.</param>
    /// <param name="dataType">Data type.</param>
    /// <param name="unit">Display unit.</param>
    /// <param name="precision">Maximum decimal places.</param>
    /// <returns>The new field, or the rule that was broken.</returns>
    public Result<TemplateField> AddField(
        string code, string label, string section, FieldDataType dataType, string unit = "", int precision = 2)
    {
        if (Status != TemplateStatus.Draft)
        {
            return TemplateErrors.NotDraft;
        }

        var field = TemplateField.Create(code, label, section, dataType, unit, precision, _fields.Count + 1);
        if (FindField(field.Code) is not null)
        {
            return TemplateErrors.DuplicateFieldCode;
        }

        _fields.Add(field);
        return field;
    }

    /// <summary>Adds a validation rule to a draft template.</summary>
    /// <param name="rule">The rule, created with one of the <see cref="ValidationRule"/> factories.</param>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result AddRule(ValidationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (Status != TemplateStatus.Draft)
        {
            return TemplateErrors.NotDraft;
        }

        if (FindField(rule.TargetFieldCode) is null)
        {
            return TemplateErrors.UnknownField;
        }

        if (_rules.Exists(r => string.Equals(r.Code, rule.Code, StringComparison.Ordinal)))
        {
            return TemplateErrors.DuplicateRuleCode;
        }

        _rules.Add(rule);
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

        Status = TemplateStatus.Published;
        return Result.Success();
    }

    /// <summary>Retires a published template when a newer version replaces it.</summary>
    public void Retire() => Status = TemplateStatus.Retired;
}
