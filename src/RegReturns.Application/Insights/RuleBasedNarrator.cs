using System.Globalization;

using RegReturns.Domain.Common;
using RegReturns.Domain.Insights;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Insights;

/// <summary>
/// Writes an insight's narrative from fixed rules (ADR 0030): the standing-in writer when the AI provider is off,
/// unconfigured or failing, and the writer when <c>Ai:Provider</c> is <c>RuleBased</c>. Deterministic: the same payload
/// always gives the same text. Likely causes and questions follow from the kind of rule that failed, and movers no rule
/// flagged are pointed out.
/// </summary>
public sealed class RuleBasedNarrator : IInsightNarrator
{
    /// <summary>The name recorded as the model of rule-based insights.</summary>
    public const string RuleSet = "regreturns-rules/1";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <inheritdoc />
    public InsightProvider Provider => InsightProvider.RuleBased;

    /// <inheritdoc />
    public string Model => RuleSet;

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <summary>Writes the narrative of a payload.</summary>
    /// <param name="request">The payload.</param>
    /// <returns>The narrative.</returns>
    public static InsightNarrative Write(InsightRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var movers = InsightFacts.Movers(request);
        var breaches = InsightFacts.Breaches(request);
        var observations = new List<string>();
        var questions = new List<string>();
        var questioned = new HashSet<string>(StringComparer.Ordinal);

        foreach (var breach in breaches)
        {
            var field = request.FindField(breach.FieldCode);
            observations.Add(Observe(breach, field, request));
            if (questioned.Add(breach.FieldCode) && Ask(breach, field, request) is { } question)
            {
                questions.Add(question);
            }
        }

        var flagged = breaches.Select(b => b.FieldCode).ToHashSet(StringComparer.Ordinal);
        foreach (var mover in movers.Where(m => !flagged.Contains(m.FieldCode)))
        {
            observations.Add(string.Create(
                Invariant,
                $"{mover.Label} {Moved(mover.ChangePercent)} against {Against(mover.Basis, request)} without triggering a rule; check whether the bank explains it elsewhere."));
            if (questioned.Add(mover.FieldCode))
            {
                questions.Add($"What explains the {Noun(mover.ChangePercent)} in {Lower(mover.Label)} since {Against(mover.Basis, request)}, and is it expected to last?");
            }
        }

        if (observations.Count == 0)
        {
            observations.Add(string.Create(
                Invariant, $"Every figure is within {InsightFacts.MoverThresholdPercent:0}% of the earlier periods it can be compared with, and no validation rule failed."));
        }

        return InsightNarrative.Clean(Headline(request, movers, breaches), observations, questions).Value;
    }

    /// <inheritdoc />
    public Task<Result<NarratorAnswer>> NarrateAsync(InsightRequest request, string payloadJson, CancellationToken cancellationToken) =>
        Task.FromResult<Result<NarratorAnswer>>(new NarratorAnswer(Write(request), RuleSet, 0, 0));

    private static string Headline(InsightRequest request, IReadOnlyList<InsightMover> movers, IReadOnlyList<InsightBreach> breaches)
    {
        var errors = breaches.Count(b => b.Severity == Severity.Error);
        var warnings = breaches.Count(b => b.Severity == Severity.Warning);
        var unjustified = breaches.Count(b => b.Severity == Severity.Warning && !b.JustifiedByBank);
        var findings = (errors, warnings, unjustified) switch
        {
            (1, _, _) => "1 validation error still stands against it",
            ( > 1, _, _) => $"{errors} validation errors still stand against it",
            (0, > 0, 0) => $"{Count(warnings, "warning")}, all justified by the bank",
            (0, > 0, _) => $"{Count(warnings, "warning")}, {unjustified} not justified",
            _ => "no validation findings",
        };

        if (movers.Count == 0)
        {
            return string.Create(
                Invariant,
                $"No figure moved by {InsightFacts.MoverThresholdPercent:0}% or more against the earlier periods; {findings}.");
        }

        var top = movers[0];
        return string.Create(
            Invariant,
            $"{top.Label} {Moved(top.ChangePercent)} against {Against(top.Basis, request)}, the largest movement in this return; {findings}.");
    }

    private static string Observe(InsightBreach breach, InsightField? field, InsightRequest request)
    {
        var rule = breach.Rule.TrimEnd('.');
        var text = breach.RuleType switch
        {
            RuleType.Variance when ChangeOf(field) is { } change =>
                $"{breach.FieldLabel} {Moved(change.Percent)} against {Against(change.Basis, request)} ({rule}). Movements this large usually come from a one-off transaction, a reclassification between lines or a keying error.",
            RuleType.Variance =>
                $"{breach.FieldLabel} moved more than the rule allows ({rule}). Movements this large usually come from a one-off transaction, a reclassification between lines or a keying error.",
            RuleType.Range =>
                $"{breach.FieldLabel} is outside its supervisory limit ({rule}). This points to a real deterioration the bank should explain, or to an item reported in the wrong line.",
            RuleType.CrossField =>
                $"{breach.FieldLabel} does not agree with related figures ({rule}). Usually a component was reported in the wrong line or a total was not refreshed.",
            RuleType.Required => $"{breach.FieldLabel} is blank ({rule}).",
            _ => $"{breach.FieldLabel} is not a valid figure ({rule}).",
        };

        if (breach.Severity == Severity.Error)
        {
            return text + " It is an error, so the return cannot be submitted as it is.";
        }

        return text + (breach.JustifiedByBank ? " The bank justified this warning." : " The bank has not justified this warning.");
    }

    private static string? Ask(InsightBreach breach, InsightField? field, InsightRequest request)
    {
        var label = Lower(breach.FieldLabel);
        return breach.RuleType switch
        {
            RuleType.Variance when ChangeOf(field) is { } change =>
                $"What explains the {Noun(change.Percent)} in {label} since {Against(change.Basis, request)}, and is it expected to last?",
            RuleType.Variance => $"What explains the movement in {label}, and is it expected to last?",
            RuleType.Range => $"What is the bank doing to bring {label} back within the supervisory limit, and by when?",
            RuleType.CrossField => $"How does the bank derive {label} from its components, and which figure is correct?",
            _ => $"Can the bank supply a valid figure for {label}?",
        };
    }

    private static (decimal Percent, VarianceBasis Basis)? ChangeOf(InsightField? field) => field switch
    {
        { ChangeVsPreviousPercent: { } change } => (change, VarianceBasis.PreviousPeriod),
        { ChangeVsLastYearPercent: { } change } => (change, VarianceBasis.SamePeriodLastYear),
        _ => null,
    };

    private static string Moved(decimal changePercent) => string.Create(
        Invariant, $"{(changePercent >= 0 ? "rose" : "fell")} by {Math.Abs(changePercent):0.#}%");

    private static string Noun(decimal changePercent) => changePercent >= 0 ? "rise" : "fall";

    private static string Against(VarianceBasis basis, InsightRequest request) => basis == VarianceBasis.PreviousPeriod
        ? request.PreviousPeriod
        : $"{request.SamePeriodLastYear} (a year earlier)";

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    // "Non-performing loan ratio" reads as "non-performing loan ratio" inside a sentence; acronyms such as "LCR" stay.
    private static string Lower(string label) =>
        label.Length > 1 && char.IsUpper(label[0]) && char.IsLower(label[1]) ? char.ToLowerInvariant(label[0]) + label[1..] : label;
}
