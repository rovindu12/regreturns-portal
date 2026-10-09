using System.Text;
using System.Text.Json;

using RegReturns.Domain.Common;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Insights;

/// <summary>
/// What a narrator writes: a headline, observations (what stands out and its likely causes) and questions a reviewer
/// could put to the bank. Plain text; the portal encodes it when showing it.
/// </summary>
/// <param name="Headline">One sentence.</param>
/// <param name="Observations">At most <see cref="MaxObservations"/> observations.</param>
/// <param name="Questions">At most <see cref="MaxQuestions"/> questions.</param>
public sealed record InsightNarrative(string Headline, IReadOnlyList<string> Observations, IReadOnlyList<string> Questions)
{
    /// <summary>Longest headline kept.</summary>
    public const int HeadlineMaxLength = 300;

    /// <summary>Longest observation or question kept.</summary>
    public const int ItemMaxLength = 600;

    /// <summary>Most observations kept.</summary>
    public const int MaxObservations = 5;

    /// <summary>Most questions kept.</summary>
    public const int MaxQuestions = 5;

    /// <summary>The error returned when a provider's answer has no usable headline.</summary>
    public static readonly Error MissingHeadline = new("Insight.InvalidOutput", "The answer had no headline.");

    /// <summary>
    /// Cleans text from a provider: collapses white space and control characters, drops blank items, keeps the first
    /// <see cref="MaxObservations"/> observations and <see cref="MaxQuestions"/> questions, and shortens anything
    /// longer than the limits at a word boundary.
    /// </summary>
    /// <param name="headline">The headline.</param>
    /// <param name="observations">The observations.</param>
    /// <param name="questions">The questions.</param>
    /// <returns>The narrative, or <see cref="MissingHeadline"/>.</returns>
    public static Result<InsightNarrative> Clean(string? headline, IEnumerable<string?>? observations, IEnumerable<string?>? questions)
    {
        var title = Tidy(headline, HeadlineMaxLength);
        if (title.Length == 0)
        {
            return MissingHeadline;
        }

        return new InsightNarrative(title, Items(observations, MaxObservations), Items(questions, MaxQuestions));
    }

    private static List<string> Items(IEnumerable<string?>? items, int max) =>
        [.. (items ?? []).Select(i => Tidy(i, ItemMaxLength)).Where(i => i.Length > 0).Take(max)];

    private static string Tidy(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text.Trim())
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = true;
                continue;
            }

            if (space && builder.Length > 0)
            {
                builder.Append(' ');
            }

            space = false;
            builder.Append(c);
        }

        if (builder.Length <= maxLength)
        {
            return builder.ToString();
        }

        var cut = builder.ToString(0, maxLength - 1);
        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > maxLength / 2 ? cut[..lastSpace] : cut).TrimEnd() + "…";
    }
}

/// <summary>A figure that moved by at least <see cref="InsightFacts.MoverThresholdPercent"/> against an earlier period.</summary>
/// <param name="FieldCode">The field code.</param>
/// <param name="Label">The field's label.</param>
/// <param name="Unit">The unit.</param>
/// <param name="Current">The figure now.</param>
/// <param name="Prior">The earlier figure.</param>
/// <param name="ChangePercent">The change in percent of the earlier figure.</param>
/// <param name="Basis">Which earlier period.</param>
public sealed record InsightMover(
    string FieldCode, string Label, string Unit, decimal Current, decimal Prior, decimal ChangePercent, VarianceBasis Basis);

/// <summary>A rule the return failed, with the field's label.</summary>
/// <param name="RuleCode">The rule code.</param>
/// <param name="RuleType">The kind of rule.</param>
/// <param name="Severity">Error or warning.</param>
/// <param name="FieldCode">The field code.</param>
/// <param name="FieldLabel">The field's label.</param>
/// <param name="Rule">The rule's text.</param>
/// <param name="JustifiedByBank">Whether the bank justified it.</param>
public sealed record InsightBreach(
    string RuleCode, RuleType RuleType, Severity Severity, string FieldCode, string FieldLabel, string Rule, bool JustifiedByBank);

/// <summary>
/// An insight as stored and shown: the narrative plus the movers and failed rules, which are computed from the payload
/// (<see cref="InsightFacts"/>), never taken from a provider.
/// </summary>
/// <param name="Schema">The content format, <see cref="CurrentSchema"/>.</param>
/// <param name="Headline">The headline.</param>
/// <param name="Observations">The observations.</param>
/// <param name="Questions">The questions for the bank.</param>
/// <param name="Movers">The largest movements, largest first.</param>
/// <param name="Breaches">The failed rules, in validation order.</param>
public sealed record InsightContent(
    string Schema,
    string Headline,
    IReadOnlyList<string> Observations,
    IReadOnlyList<string> Questions,
    IReadOnlyList<InsightMover> Movers,
    IReadOnlyList<InsightBreach> Breaches)
{
    /// <summary>The content format written by this version.</summary>
    public const string CurrentSchema = "regreturns.insight/1";

    /// <summary>Puts a narrative together with the facts of its payload.</summary>
    /// <param name="request">The payload.</param>
    /// <param name="narrative">The narrative.</param>
    /// <returns>The content.</returns>
    public static InsightContent Compose(InsightRequest request, InsightNarrative narrative)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(narrative);
        return new InsightContent(
            CurrentSchema, narrative.Headline, narrative.Observations, narrative.Questions, InsightFacts.Movers(request), InsightFacts.Breaches(request));
    }

    /// <summary>Reads stored content.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The content.</returns>
    public static InsightContent FromJson(string json) =>
        JsonSerializer.Deserialize<InsightContent>(json, InsightJson.Options)
        ?? throw new JsonException("Insight content is empty.");

    /// <summary>Serialises the content as stored and hashed.</summary>
    /// <returns>The JSON.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, InsightJson.Options);
}

/// <summary>The parts of an insight computed from the payload: movers and failed rules.</summary>
public static class InsightFacts
{
    /// <summary>Smallest change, in percent, that makes a figure a mover.</summary>
    public const decimal MoverThresholdPercent = 10m;

    /// <summary>Most movers listed.</summary>
    public const int MaxMovers = 5;

    /// <summary>
    /// Returns the figures that moved by at least <see cref="MoverThresholdPercent"/>, largest change first: against
    /// the previous period, or against the same period last year when the previous period has no approved figure.
    /// </summary>
    /// <param name="request">The payload.</param>
    /// <returns>At most <see cref="MaxMovers"/> movers.</returns>
    public static IReadOnlyList<InsightMover> Movers(InsightRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return [.. request.Fields
            .Select((field, index) => (Mover: MoverOf(field), Index: index))
            .Where(x => x.Mover is not null && Math.Abs(x.Mover.ChangePercent) >= MoverThresholdPercent)
            .OrderByDescending(x => Math.Abs(x.Mover!.ChangePercent))
            .ThenBy(x => x.Index)
            .Take(MaxMovers)
            .Select(x => x.Mover!)];
    }

    /// <summary>Returns the failed rules with their fields' labels.</summary>
    /// <param name="request">The payload.</param>
    /// <returns>The breaches, in validation order.</returns>
    public static IReadOnlyList<InsightBreach> Breaches(InsightRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return [.. request.Findings.Select(f => new InsightBreach(
            f.RuleCode, f.RuleType, f.Severity, f.FieldCode, request.FindField(f.FieldCode)?.Label ?? f.FieldCode, f.Rule, f.JustifiedByBank))];
    }

    private static InsightMover? MoverOf(InsightField field) => field switch
    {
        { Current: { } now, PreviousPeriod: { } before, ChangeVsPreviousPercent: { } change } =>
            new InsightMover(field.Code, field.Label, field.Unit, now, before, change, VarianceBasis.PreviousPeriod),
        { Current: { } now, SamePeriodLastYear: { } before, ChangeVsLastYearPercent: { } change } =>
            new InsightMover(field.Code, field.Label, field.Unit, now, before, change, VarianceBasis.SamePeriodLastYear),
        _ => null,
    };
}
