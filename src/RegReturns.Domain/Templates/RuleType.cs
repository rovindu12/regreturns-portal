namespace RegReturns.Domain.Templates;

/// <summary>The kind of check a validation rule performs.</summary>
public enum RuleType
{
    /// <summary>The field must have a value.</summary>
    Required = 1,

    /// <summary>The value must lie between a minimum and/or maximum.</summary>
    Range = 2,

    /// <summary>The value must parse as the field's data type and precision.</summary>
    DataType = 3,

    /// <summary>Two expressions over field values must compare as configured, within a tolerance.</summary>
    CrossField = 4,

    /// <summary>The change against a prior period must not exceed a threshold.</summary>
    Variance = 5,
}

/// <summary>Whether a failed rule blocks submission.</summary>
public enum Severity
{
    /// <summary>Allows submission once the checker writes a justification.</summary>
    Warning = 1,

    /// <summary>Blocks submission until fixed.</summary>
    Error = 2,
}

/// <summary>Comparison used by a cross-field rule.</summary>
public enum ComparisonOperator
{
    /// <summary>Left equals right within the tolerance.</summary>
    Equal = 1,

    /// <summary>Left is less than or equal to right (plus tolerance).</summary>
    LessThanOrEqual = 2,

    /// <summary>Left is greater than or equal to right (minus tolerance).</summary>
    GreaterThanOrEqual = 3,
}

/// <summary>Which earlier period a variance rule compares against.</summary>
public enum VarianceBasis
{
    /// <summary>The immediately preceding period.</summary>
    PreviousPeriod = 1,

    /// <summary>The same period in the previous year.</summary>
    SamePeriodLastYear = 2,
}
