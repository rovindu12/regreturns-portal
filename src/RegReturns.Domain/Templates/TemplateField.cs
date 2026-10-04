using RegReturns.Domain.Common;

namespace RegReturns.Domain.Templates;

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

    /// <summary>Gets the display order within the template.</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>Gets a value indicating whether the field holds a number.</summary>
    public bool IsNumeric => DataType is FieldDataType.Amount or FieldDataType.WholeNumber or FieldDataType.Percentage;

    internal static TemplateField Create(
        string code, string label, string section, FieldDataType dataType, string unit, int precision, int displayOrder)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (precision is < 0 or > 4)
        {
            throw new DomainException("Precision must be between 0 and 4.");
        }

        return new TemplateField
        {
            Code = Guard.Code(code, CodeMaxLength),
            Label = Guard.NotBlank(label, LabelMaxLength),
            Section = Guard.NotBlank(section, SectionMaxLength),
            DataType = dataType,
            Unit = unit.Length > UnitMaxLength
                ? throw new DomainException($"Unit must be at most {UnitMaxLength} characters.")
                : unit.Trim(),
            Precision = dataType == FieldDataType.WholeNumber ? 0 : precision,
            DisplayOrder = displayOrder,
        };
    }
}
