using RegReturns.Domain.Common;

namespace RegReturns.Domain.Templates;

/// <summary>What an administrator enters to add or change a template field.</summary>
/// <param name="Code">Field code, unique within the template (A-Z, 0-9 and '_', starting with a letter).</param>
/// <param name="Label">Label shown to users.</param>
/// <param name="Section">Section heading the field is grouped under.</param>
/// <param name="DataType">Data type.</param>
/// <param name="Unit">Display unit, such as <c>VLD m</c> or <c>%</c>.</param>
/// <param name="Precision">Maximum decimal places for numeric fields (0-4; whole numbers always 0).</param>
public sealed record FieldDefinition(
    string Code, string Label, string Section, FieldDataType DataType, string Unit = "", int Precision = 2);

/// <summary>A single line item captured by a template version.</summary>
public sealed class TemplateField : Entity
{
    /// <summary>Maximum length of a field code.</summary>
    public const int CodeMaxLength = 40;

    /// <summary>Maximum length of a label.</summary>
    public const int LabelMaxLength = 200;

    /// <summary>Maximum length of a section name.</summary>
    public const int SectionMaxLength = 100;

    /// <summary>Maximum length of a unit.</summary>
    public const int UnitMaxLength = 20;

    /// <summary>Maximum number of decimal places (the value columns hold four).</summary>
    public const int MaxPrecision = 4;

    private TemplateField()
    {
        Code = string.Empty;
        Label = string.Empty;
        Section = string.Empty;
        Unit = string.Empty;
    }

    /// <summary>Gets the owning template version id.</summary>
    public Guid TemplateVersionId { get; }

    /// <summary>Gets the field code, unique within the template (for example <c>TOTAL_HQLA</c>).</summary>
    public string Code { get; private set; }

    /// <summary>Gets the label shown to users.</summary>
    public string Label { get; private set; }

    /// <summary>Gets the section heading the field is grouped under.</summary>
    public string Section { get; private set; }

    /// <summary>Gets the data type.</summary>
    public FieldDataType DataType { get; private set; }

    /// <summary>Gets the display unit (for example <c>VLD m</c> or <c>%</c>).</summary>
    public string Unit { get; private set; }

    /// <summary>Gets the maximum number of decimal places for numeric fields.</summary>
    public int Precision { get; private set; }

    /// <summary>Gets the display order within the template, starting at 1.</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>Gets a value indicating whether the field holds a number.</summary>
    public bool IsNumeric => IsNumericType(DataType);

    /// <summary>Returns whether a data type holds a number.</summary>
    /// <param name="dataType">The data type.</param>
    /// <returns><see langword="true"/> for amounts, whole numbers and percentages.</returns>
    public static bool IsNumericType(FieldDataType dataType) =>
        dataType is FieldDataType.Amount or FieldDataType.WholeNumber or FieldDataType.Percentage;

    /// <summary>Checks a definition, returning the first problem or <see langword="null"/>.</summary>
    internal static Error? Check(FieldDefinition definition)
    {
        var code = definition.Code?.Trim();
        if (!Guard.IsCode(code, CodeMaxLength))
        {
            return TemplateErrors.InvalidField.WithMessage(
                $"The code must be 1-{CodeMaxLength} characters of A-Z, 0-9 and '_', starting with a letter.");
        }

        if (string.IsNullOrWhiteSpace(definition.Label) || definition.Label.Trim().Length > LabelMaxLength)
        {
            return TemplateErrors.InvalidField.WithMessage($"The label is required and must be at most {LabelMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(definition.Section) || definition.Section.Trim().Length > SectionMaxLength)
        {
            return TemplateErrors.InvalidField.WithMessage($"The section is required and must be at most {SectionMaxLength} characters.");
        }

        if ((definition.Unit?.Trim().Length ?? 0) > UnitMaxLength)
        {
            return TemplateErrors.InvalidField.WithMessage($"The unit must be at most {UnitMaxLength} characters.");
        }

        if (!Enum.IsDefined(definition.DataType))
        {
            return TemplateErrors.InvalidField.WithMessage("Choose a data type.");
        }

        return definition.Precision is < 0 or > MaxPrecision
            ? TemplateErrors.InvalidField.WithMessage($"Decimal places must be between 0 and {MaxPrecision}.")
            : null;
    }

    /// <summary>Creates a field from a definition that passed <see cref="Check"/>.</summary>
    internal static TemplateField Create(FieldDefinition definition, int displayOrder)
    {
        if (Check(definition) is { } error)
        {
            throw new DomainException(error.Message);
        }

        var field = new TemplateField { Code = definition.Code.Trim(), DisplayOrder = displayOrder };
        field.Apply(definition);
        return field;
    }

    /// <summary>Changes everything except the code, from a definition that passed <see cref="Check"/>.</summary>
    internal void Update(FieldDefinition definition)
    {
        if (Check(definition) is { } error)
        {
            throw new DomainException(error.Message);
        }

        Apply(definition);
    }

    /// <summary>Moves the field to a display position.</summary>
    internal void MoveTo(int displayOrder) => DisplayOrder = displayOrder;

    /// <summary>Copies the field for a new draft version.</summary>
    internal TemplateField Copy() => new()
    {
        Code = Code,
        Label = Label,
        Section = Section,
        DataType = DataType,
        Unit = Unit,
        Precision = Precision,
        DisplayOrder = DisplayOrder,
    };

    /// <summary>Returns this field as a definition, for example to pre-fill an edit form.</summary>
    /// <returns>The definition.</returns>
    public FieldDefinition ToDefinition() => new(Code, Label, Section, DataType, Unit, Precision);

    private void Apply(FieldDefinition definition)
    {
        Label = definition.Label.Trim();
        Section = definition.Section.Trim();
        DataType = definition.DataType;
        Unit = definition.Unit?.Trim() ?? string.Empty;
        Precision = IsNumericType(definition.DataType) && definition.DataType != FieldDataType.WholeNumber
            ? definition.Precision
            : 0;
    }
}
