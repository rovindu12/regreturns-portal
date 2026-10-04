namespace RegReturns.Domain.Templates;

/// <summary>The data type of a template field.</summary>
public enum FieldDataType
{
    /// <summary>A monetary amount with decimals (VLD millions unless stated).</summary>
    Amount = 1,

    /// <summary>A whole number.</summary>
    WholeNumber = 2,

    /// <summary>A percentage stored as a number (12.5 means 12.5%).</summary>
    Percentage = 3,

    /// <summary>Free text.</summary>
    Text = 4,

    /// <summary>A calendar date in ISO 8601 format.</summary>
    Date = 5,

    /// <summary>Yes or no.</summary>
    Boolean = 6,
}
