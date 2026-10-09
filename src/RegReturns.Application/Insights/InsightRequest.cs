using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Insights;

/// <summary>
/// What an insight provider receives about a return (ADR 0030): codes, template text and figures, never the bank's
/// identity, people, comments, justification texts or the values of text fields. Built only by
/// <see cref="InsightPayloadBuilder"/> and checked by <see cref="InsightPayloadGuard"/> before it is sent.
/// </summary>
/// <param name="Schema">The payload format, <see cref="CurrentSchema"/>.</param>
/// <param name="ReturnType">The return type's code and name.</param>
/// <param name="Frequency">How often it is filed.</param>
/// <param name="Period">The reporting period, such as <c>2026-08</c>.</param>
/// <param name="PreviousPeriod">The previous period.</param>
/// <param name="SamePeriodLastYear">The same period a year earlier.</param>
/// <param name="Revision">The revision described.</param>
/// <param name="Fields">Every numeric field of the template, in display order.</param>
/// <param name="Findings">The current revision's findings, in the order validation reports them.</param>
public sealed record InsightRequest(
    string Schema,
    InsightReturnType ReturnType,
    ReturnFrequency Frequency,
    string Period,
    string PreviousPeriod,
    string SamePeriodLastYear,
    int Revision,
    IReadOnlyList<InsightField> Fields,
    IReadOnlyList<InsightFinding> Findings)
{
    /// <summary>The payload format written by this version.</summary>
    public const string CurrentSchema = "regreturns.insight-request/1";

    /// <summary>Returns a field by code.</summary>
    /// <param name="code">The field code.</param>
    /// <returns>The field, or <see langword="null"/>.</returns>
    public InsightField? FindField(string code) => Fields.FirstOrDefault(f => f.Code == code);
}

/// <summary>A return type in an insight payload.</summary>
/// <param name="Code">The code, such as <c>MDA</c>.</param>
/// <param name="Name">The name.</param>
public sealed record InsightReturnType(string Code, string Name);

/// <summary>A numeric field with its figure and the figures it is compared with.</summary>
/// <param name="Code">The field code.</param>
/// <param name="Label">The label.</param>
/// <param name="Section">The section of the form.</param>
/// <param name="Unit">The unit, such as <c>VLD m</c> or <c>%</c>.</param>
/// <param name="Current">The figure in this return, if given.</param>
/// <param name="PreviousPeriod">The approved figure of the previous period, if any.</param>
/// <param name="SamePeriodLastYear">The approved figure of the same period last year, if any.</param>
/// <param name="ChangeVsPreviousPercent">The change against the previous period, in percent of its absolute value.</param>
/// <param name="ChangeVsLastYearPercent">The change against the same period last year, in percent of its absolute value.</param>
public sealed record InsightField(
    string Code,
    string Label,
    string Section,
    string Unit,
    decimal? Current,
    decimal? PreviousPeriod,
    decimal? SamePeriodLastYear,
    decimal? ChangeVsPreviousPercent,
    decimal? ChangeVsLastYearPercent);

/// <summary>A failed validation rule.</summary>
/// <param name="RuleCode">The rule code.</param>
/// <param name="RuleType">What kind of rule it is.</param>
/// <param name="Severity">Error or warning.</param>
/// <param name="FieldCode">The field it is reported against.</param>
/// <param name="Rule">The rule's text from the template.</param>
/// <param name="JustifiedByBank">Whether the bank justified the warning (its text is never sent).</param>
public sealed record InsightFinding(
    string RuleCode, RuleType RuleType, Severity Severity, string FieldCode, string Rule, bool JustifiedByBank);

/// <summary>Builds the payload of a return; pure, so every channel and test builds the same document.</summary>
public static class InsightPayloadBuilder
{
    private static readonly FieldDataType[] NumericTypes = [FieldDataType.Amount, FieldDataType.WholeNumber, FieldDataType.Percentage];

    /// <summary>Builds the payload.</summary>
    /// <param name="returnType">The return type.</param>
    /// <param name="template">The template version the return is captured with, with fields and rules.</param>
    /// <param name="submission">The return, with values and findings.</param>
    /// <param name="period">Its reporting period.</param>
    /// <param name="priorFigures">The bank's approved figures per comparison basis (see the variance rules).</param>
    /// <returns>The payload.</returns>
    public static InsightRequest Build(
        ReturnType returnType,
        TemplateVersion template,
        Submission submission,
        ReportingPeriod period,
        IReadOnlyDictionary<VarianceBasis, IReadOnlyDictionary<string, decimal>> priorFigures)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(priorFigures);

        var previous = priorFigures.GetValueOrDefault(VarianceBasis.PreviousPeriod);
        var lastYear = priorFigures.GetValueOrDefault(VarianceBasis.SamePeriodLastYear);
        var fields = template.Fields
            .Where(f => NumericTypes.Contains(f.DataType))
            .OrderBy(f => f.DisplayOrder)
            .Select(f =>
            {
                var current = Normalise(FieldValueParser.Parse(f, submission.FindValue(f.Code)?.RawValue).Number);
                var before = Figure(previous, f.Code);
                var yearBefore = Figure(lastYear, f.Code);
                return new InsightField(
                    f.Code, f.Label, f.Section, f.Unit, current, before, yearBefore, ChangePercent(current, before), ChangePercent(current, yearBefore));
            })
            .ToList();

        var order = template.Fields.ToDictionary(f => f.Code, f => f.DisplayOrder, StringComparer.Ordinal);
        var rules = template.Rules.ToDictionary(r => r.Id);
        var findings = submission.CurrentFindings
            .Where(f => rules.ContainsKey(f.RuleId))
            .Select(f => (Finding: f, Rule: rules[f.RuleId]))
            .OrderBy(x => order.GetValueOrDefault(x.Finding.FieldCode, int.MaxValue))
            .ThenBy(x => x.Rule.RuleType)
            .ThenBy(x => x.Rule.Code, StringComparer.Ordinal)
            .Select(x => new InsightFinding(
                x.Rule.Code, x.Rule.RuleType, x.Finding.Severity, x.Finding.FieldCode, x.Rule.Message, x.Finding.Justification is not null))
            .ToList();

        return new InsightRequest(
            InsightRequest.CurrentSchema,
            new InsightReturnType(returnType.Code, returnType.Name),
            period.Frequency,
            period.Label,
            period.Previous().Label,
            period.SamePeriodLastYear().Label,
            submission.Revision,
            fields,
            findings);
    }

    /// <summary>
    /// Returns the change from <paramref name="before"/> to <paramref name="current"/> in percent of the absolute
    /// earlier figure, rounded to one decimal; <see langword="null"/> when either is missing or the earlier one is zero.
    /// </summary>
    /// <param name="current">The figure now.</param>
    /// <param name="before">The earlier figure.</param>
    /// <returns>The change in percent.</returns>
    public static decimal? ChangePercent(decimal? current, decimal? before) =>
        current is { } now && before is { } then && then != 0m
            ? Normalise(Math.Round((now - then) / Math.Abs(then) * 100m, 1, MidpointRounding.AwayFromZero))
            : null;

    private static decimal? Figure(IReadOnlyDictionary<string, decimal>? figures, string code) =>
        figures is not null && figures.TryGetValue(code, out var value) ? Normalise(value) : null;

    // The database keeps four decimal places; drop trailing zeros so the same figure always serialises the same way.
    private static decimal? Normalise(decimal? value) =>
        value is { } number ? decimal.Parse(number.ToString("0.############################", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) : null;
}

/// <summary>The JSON form of insight payloads and contents: compact, camel case, enums by name, stable property order.</summary>
public static class InsightJson
{
    /// <summary>Gets the serializer options.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    /// <summary>Serialises a payload exactly as it is sent and hashed.</summary>
    /// <param name="request">The payload.</param>
    /// <returns>The JSON.</returns>
    public static string Serialize(InsightRequest request) => JsonSerializer.Serialize(request, Options);
}
