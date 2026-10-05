namespace RegReturns.Api.Contracts;

/// <summary>A return type banks file.</summary>
/// <param name="Code">The code, such as <c>MLR</c>.</param>
/// <param name="Name">The name.</param>
/// <param name="Description">What the return covers.</param>
/// <param name="Frequency">How often it is filed: <c>Monthly</c> or <c>Quarterly</c>.</param>
/// <param name="DueDaysAfterPeriodEnd">How many days after the period ends it is due.</param>
/// <param name="Versions">The published template versions, newest first.</param>
public sealed record ReturnTypeResponse(
    string Code, string Name, string Description, string Frequency, int DueDaysAfterPeriodEnd, IReadOnlyList<TemplateVersionResponse> Versions);

/// <summary>A published template version.</summary>
/// <param name="Version">The version number.</param>
/// <param name="EffectiveFrom">The first reporting-period start it applies to.</param>
public sealed record TemplateVersionResponse(int Version, DateOnly EffectiveFrom);

/// <summary>The template a return for a period is filed with.</summary>
/// <param name="ReturnType">The return type code.</param>
/// <param name="Version">The template version number.</param>
/// <param name="EffectiveFrom">The first reporting-period start it applies to.</param>
/// <param name="Period">The period it applies to here, such as <c>2027-03</c> or <c>2027-Q1</c>.</param>
/// <param name="Fields">The fields, in display order.</param>
/// <param name="Rules">The active validation rules.</param>
public sealed record TemplateResponse(
    string ReturnType, int Version, DateOnly EffectiveFrom, string Period, IReadOnlyList<FieldResponse> Fields, IReadOnlyList<RuleResponse> Rules);

/// <summary>A field of a template.</summary>
/// <param name="Code">The code values are keyed by.</param>
/// <param name="Label">The label.</param>
/// <param name="Section">The section.</param>
/// <param name="DataType">The data type: <c>Amount</c>, <c>WholeNumber</c>, <c>Percentage</c>, <c>Text</c>, <c>Date</c> or <c>Boolean</c>.</param>
/// <param name="Unit">The unit, such as <c>VLD</c> or <c>%</c>.</param>
/// <param name="Precision">The most decimal places a number may have.</param>
/// <param name="Required">Whether a value is required.</param>
public sealed record FieldResponse(string Code, string Label, string Section, string DataType, string Unit, int Precision, bool Required);

/// <summary>A validation rule of a template.</summary>
/// <param name="Code">The rule code findings carry.</param>
/// <param name="Type">The kind of check: <c>Required</c>, <c>DataType</c>, <c>Range</c>, <c>CrossField</c> or <c>Variance</c>.</param>
/// <param name="Severity"><c>Error</c> (blocks submission) or <c>Warning</c> (needs a justification).</param>
/// <param name="Field">The field findings are reported against.</param>
/// <param name="Message">The message findings carry.</param>
public sealed record RuleResponse(string Code, string Type, string Severity, string Field, string Message);
