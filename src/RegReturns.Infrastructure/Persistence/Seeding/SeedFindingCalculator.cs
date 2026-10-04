using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Infrastructure.Persistence.Seeding;

/// <summary>
/// Works out which warning rules the generated demo figures trip, so seeded history carries the same
/// findings (and justifications) the validation engine would have produced.
/// Error rules are not evaluated: the figure generator guarantees they pass, and tests check it.
/// </summary>
internal static class SeedFindingCalculator
{
    public static IReadOnlyList<FindingDraft> Warnings(
        TemplateVersion template,
        IReadOnlyDictionary<string, decimal> values,
        Func<VarianceBasis, IReadOnlyDictionary<string, decimal>?> priorValues)
    {
        return template.Rules
            .Where(r => r.IsActive && r.Severity == Severity.Warning && Fails(r, values, priorValues))
            .Select(r => new FindingDraft(r.Id, r.Code, r.TargetFieldCode, r.Severity, r.Message))
            .ToList();
    }

    private static bool Fails(
        ValidationRule rule,
        IReadOnlyDictionary<string, decimal> values,
        Func<VarianceBasis, IReadOnlyDictionary<string, decimal>?> priorValues)
    {
        var value = values[rule.TargetFieldCode];
        switch (rule.RuleType)
        {
            case RuleType.Range:
                return value < rule.MinValue || value > rule.MaxValue;

            case RuleType.Variance:
                var prior = priorValues(rule.VarianceBasis!.Value);
                if (prior is null || !prior.TryGetValue(rule.TargetFieldCode, out var previous) || previous == 0m)
                {
                    return false;
                }

                return Math.Abs(value - previous) / Math.Abs(previous) * 100m > rule.ThresholdPercent;

            case RuleType.CrossField when rule.Code == MlrTemplate.RuleL2bCap:
                return values[MlrTemplate.L2bHqla] > 0.15m * values[MlrTemplate.TotalHqla];

            default:
                throw new NotSupportedException(
                    $"Seed data cannot evaluate warning rule {rule.Code} ({rule.RuleType}). Extend {nameof(SeedFindingCalculator)}.");
        }
    }
}
