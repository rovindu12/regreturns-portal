using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.Domain.Validation;

namespace RegReturns.UnitTests.Domain;

public sealed class ValidationEngineTests
{
    private static TemplateVersion Template(params ValidationRule[] rules)
    {
        var template = TemplateVersion.CreateDraft(Guid.CreateVersion7(), 1, new DateOnly(2026, 1, 1));
        template.AddField("A", "A", "S", FieldDataType.Amount).IsSuccess.ShouldBeTrue();
        template.AddField("B", "B", "S", FieldDataType.Amount).IsSuccess.ShouldBeTrue();
        template.AddField("TOTAL", "Total", "S", FieldDataType.Amount).IsSuccess.ShouldBeTrue();
        template.AddField("RATIO", "Ratio", "S", FieldDataType.Percentage).IsSuccess.ShouldBeTrue();
        template.AddField("DATE", "Date", "S", FieldDataType.Date).IsSuccess.ShouldBeTrue();
        foreach (var rule in rules)
        {
            template.AddRule(rule).IsSuccess.ShouldBeTrue();
        }

        template.Publish().IsSuccess.ShouldBeTrue();
        return template;
    }

    private static Dictionary<string, string?> Values(params (string Code, string? Value)[] values) =>
        values.ToDictionary(v => v.Code, v => v.Value, StringComparer.Ordinal);

    private static ValidationRule Sum(decimal tolerance = 0m, ComparisonOperator op = ComparisonOperator.Equal, Severity severity = Severity.Error) =>
        ValidationRule.CrossField("SUM", "TOTAL", severity, "[TOTAL]", op, "[A] + [B]", tolerance, "Total must equal A + B.");

    private static ValidationRule Variance(VarianceBasis basis = VarianceBasis.PreviousPeriod) =>
        ValidationRule.Variance("A_VAR", "A", Severity.Warning, 25m, basis, "A moved by more than 25%.");

    private static Dictionary<VarianceBasis, IReadOnlyDictionary<string, decimal>> Prior(VarianceBasis basis, decimal a) =>
        new() { [basis] = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["A"] = a } };

    [Fact]
    public void Required_fails_on_a_blank_or_missing_value()
    {
        var template = Template(ValidationRule.Required("A_REQ", "A", "A is required."));

        ValidationEngine.Validate(template, Values(("A", "  "))).Single().RuleCode.ShouldBe("A_REQ");
        ValidationEngine.Validate(template, Values()).Single().RuleCode.ShouldBe("A_REQ");
        ValidationEngine.Validate(template, Values(("A", "0"))).ShouldBeEmpty();
    }

    [Fact]
    public void Data_type_fails_only_on_a_value_that_is_present_and_invalid()
    {
        var template = Template(ValidationRule.DataType("A_TYPE", "A", "A must be a number."), ValidationRule.DataType("D_TYPE", "DATE", "Bad date."));

        ValidationEngine.Validate(template, Values(("A", "12.345"), ("DATE", "31/12/2026"))).Select(f => f.RuleCode).ShouldBe(["A_TYPE", "D_TYPE"]);
        ValidationEngine.Validate(template, Values(("A", ""), ("DATE", null))).ShouldBeEmpty();
        ValidationEngine.Validate(template, Values(("A", "1,234.50"), ("DATE", "2026-12-31"))).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("-0.01", true)]
    [InlineData("0", false)]
    [InlineData("100", false)]
    [InlineData("100.01", true)]
    public void Range_limits_are_inclusive(string value, bool fails)
    {
        var template = Template(ValidationRule.Range("A_RANGE", "A", Severity.Error, 0m, 100m, "A must be 0-100."));

        ValidationEngine.Validate(template, Values(("A", value))).Count.ShouldBe(fails ? 1 : 0);
    }

    [Fact]
    public void Range_ignores_blank_and_invalid_values_so_they_are_reported_once()
    {
        var template = Template(ValidationRule.Range("A_RANGE", "A", Severity.Error, 0m, null, "A cannot be negative."));

        ValidationEngine.Validate(template, Values(("A", ""))).ShouldBeEmpty();
        ValidationEngine.Validate(template, Values(("A", "-1x"))).ShouldBeEmpty();
    }

    [Fact]
    public void Range_reads_percent_signs_on_percentages()
    {
        var template = Template(ValidationRule.Range("R_MIN", "RATIO", Severity.Warning, 100m, null, "Below 100%."));

        ValidationEngine.Validate(template, Values(("RATIO", "99.5%"))).Single().Severity.ShouldBe(Severity.Warning);
    }

    [Theory]
    [InlineData("10", "4", "14", 0, false)]
    [InlineData("10", "4", "14.05", 0.05, false)]
    [InlineData("10", "4", "13.95", 0.05, false)]
    [InlineData("10", "4", "14.06", 0.05, true)]
    [InlineData("10", "4", "15", 0, true)]
    public void Cross_field_equality_passes_within_the_tolerance(string a, string b, string total, double tolerance, bool fails)
    {
        var template = Template(Sum((decimal)tolerance));

        ValidationEngine.Validate(template, Values(("A", a), ("B", b), ("TOTAL", total))).Count.ShouldBe(fails ? 1 : 0);
    }

    [Theory]
    [InlineData("14", false)]
    [InlineData("14.5", false)]
    [InlineData("14.51", true)]
    [InlineData("1", false)]
    public void Cross_field_less_than_or_equal_allows_the_tolerance_above(string total, bool fails)
    {
        var template = Template(Sum(0.5m, ComparisonOperator.LessThanOrEqual));

        ValidationEngine.Validate(template, Values(("A", "10"), ("B", "4"), ("TOTAL", total))).Count.ShouldBe(fails ? 1 : 0);
    }

    [Theory]
    [InlineData("14", false)]
    [InlineData("13.5", false)]
    [InlineData("13.49", true)]
    [InlineData("100", false)]
    public void Cross_field_greater_than_or_equal_allows_the_tolerance_below(string total, bool fails)
    {
        var template = Template(Sum(0.5m, ComparisonOperator.GreaterThanOrEqual));

        ValidationEngine.Validate(template, Values(("A", "10"), ("B", "4"), ("TOTAL", total))).Count.ShouldBe(fails ? 1 : 0);
    }

    [Fact]
    public void Cross_field_rules_are_skipped_when_a_value_is_missing_or_invalid()
    {
        var template = Template(Sum());

        ValidationEngine.Validate(template, Values(("A", "10"), ("TOTAL", "99"))).ShouldBeEmpty();
        ValidationEngine.Validate(template, Values(("A", "10"), ("B", "abc"), ("TOTAL", "99"))).ShouldBeEmpty();
    }

    [Fact]
    public void Cross_field_rules_are_skipped_on_division_by_zero()
    {
        var template = Template(ValidationRule.CrossField(
            "RATIO_CALC", "RATIO", Severity.Error, "[RATIO]", ComparisonOperator.Equal, "[A] / [B] * 100", 0.01m, "Ratio must be A / B x 100."));

        ValidationEngine.Validate(template, Values(("A", "5"), ("B", "0"), ("RATIO", "12"))).ShouldBeEmpty();
    }

    [Fact]
    public void Cross_field_findings_show_the_entered_and_calculated_values()
    {
        var finding = ValidationEngine.Validate(Template(Sum()), Values(("A", "1000"), ("B", "234.5"), ("TOTAL", "1200"))).Single();

        finding.Message.ShouldBe("Total must equal A + B. Entered 1,200; calculated 1,234.5.");
    }

    [Fact]
    public void Cross_field_findings_with_an_expression_on_the_left_show_both_sides()
    {
        var template = Template(ValidationRule.CrossField(
            "PARTS", "TOTAL", Severity.Warning, "[A] + [B]", ComparisonOperator.LessThanOrEqual, "[TOTAL]", 0m, "Parts exceed the total."));

        ValidationEngine.Validate(template, Values(("A", "10"), ("B", "4"), ("TOTAL", "12"))).Single().Message
            .ShouldBe("Parts exceed the total. Left side 14; right side 12.");
    }

    [Theory]
    [InlineData("125", false)]
    [InlineData("75", false)]
    [InlineData("125.01", true)]
    [InlineData("74.99", true)]
    public void Variance_flags_moves_beyond_the_threshold_either_way(string current, bool fails)
    {
        var findings = ValidationEngine.Validate(Template(Variance()), Values(("A", current)), Prior(VarianceBasis.PreviousPeriod, 100m));

        findings.Count.ShouldBe(fails ? 1 : 0);
    }

    [Fact]
    public void Variance_compares_against_the_absolute_prior_value()
    {
        var findings = ValidationEngine.Validate(Template(Variance()), Values(("A", "-50")), Prior(VarianceBasis.PreviousPeriod, -100m));

        findings.Single().Message.ShouldContain("Change +50.00%");
    }

    [Fact]
    public void Variance_uses_the_rules_own_basis()
    {
        var template = Template(Variance(VarianceBasis.SamePeriodLastYear));

        ValidationEngine.Validate(template, Values(("A", "200")), Prior(VarianceBasis.PreviousPeriod, 100m)).ShouldBeEmpty();
        ValidationEngine.Validate(template, Values(("A", "200")), Prior(VarianceBasis.SamePeriodLastYear, 100m)).Single().Message
            .ShouldBe("A moved by more than 25%. Change +100.00% against the same period last year (limit 25%).");
    }

    [Fact]
    public void Variance_is_skipped_without_a_prior_figure()
    {
        var template = Template(Variance());

        ValidationEngine.Validate(template, Values(("A", "200"))).ShouldBeEmpty();
        ValidationEngine.Validate(template, Values(("A", "200")), new Dictionary<VarianceBasis, IReadOnlyDictionary<string, decimal>>
        {
            [VarianceBasis.PreviousPeriod] = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["B"] = 1m },
        }).ShouldBeEmpty();
    }

    [Fact]
    public void Variance_is_skipped_when_the_prior_figure_is_zero()
    {
        ValidationEngine.Validate(Template(Variance()), Values(("A", "200")), Prior(VarianceBasis.PreviousPeriod, 0m)).ShouldBeEmpty();
    }

    [Fact]
    public void Variance_is_skipped_when_the_current_value_is_blank_or_invalid()
    {
        var prior = Prior(VarianceBasis.PreviousPeriod, 100m);

        ValidationEngine.Validate(Template(Variance()), Values(("A", "")), prior).ShouldBeEmpty();
        ValidationEngine.Validate(Template(Variance()), Values(("A", "lots")), prior).ShouldBeEmpty();
    }

    [Fact]
    public void Inactive_rules_are_not_evaluated()
    {
        var template = TemplateVersion.CreateDraft(Guid.CreateVersion7(), 1, new DateOnly(2026, 1, 1));
        template.AddField("A", "A", "S", FieldDataType.Amount);
        template.AddRule(ValidationRule.Required("A_REQ", "A", "A is required."));
        template.SetRuleActive("A_REQ", false);

        ValidationEngine.Validate(template, Values()).ShouldBeEmpty();
    }

    [Fact]
    public void Findings_follow_field_order_and_carry_the_rule()
    {
        var bRequired = ValidationRule.Required("B_REQ", "B", "B is required.");
        var aRange = ValidationRule.Range("A_RANGE", "A", Severity.Warning, 0m, null, "A cannot be negative.");
        var aRequiredType = ValidationRule.DataType("A_TYPE", "A", "A must be a number.");
        var template = Template(bRequired, aRange, aRequiredType);

        var findings = ValidationEngine.Validate(template, Values(("A", "-5")));

        findings.Select(f => f.RuleCode).ShouldBe(["A_RANGE", "B_REQ"]);
        findings[0].ShouldBe(new FindingDraft(aRange.Id, "A_RANGE", "A", Severity.Warning, "A cannot be negative."));
    }

    [Fact]
    public void Values_for_fields_outside_the_template_are_ignored()
    {
        ValidationEngine.Validate(Template(Sum()), Values(("UNKNOWN", "1"))).ShouldBeEmpty();
    }

    [Fact]
    public void A_detail_that_would_overflow_the_message_column_is_left_out()
    {
        var message = new string('m', ValidationRule.MessageMaxLength - 5);
        var template = Template(ValidationRule.CrossField("SUM", "TOTAL", Severity.Error, "[TOTAL]", ComparisonOperator.Equal, "[A] + [B]", 0m, message));

        ValidationEngine.Validate(template, Values(("A", "1"), ("B", "1"), ("TOTAL", "3"))).Single().Message.ShouldBe(message);
    }

    [Fact]
    public void Findings_on_one_field_are_ordered_by_rule_type_then_code()
    {
        var template = Template(
            ValidationRule.Range("Z_MAX", "A", Severity.Warning, null, 10m, "Above 10."),
            ValidationRule.Variance("A_VAR", "A", Severity.Warning, 5m, VarianceBasis.PreviousPeriod, "Moved."),
            ValidationRule.Range("B_MAX", "A", Severity.Warning, null, 20m, "Above 20."));

        var findings = ValidationEngine.Validate(template, Values(("A", "50")), Prior(VarianceBasis.PreviousPeriod, 1m));

        findings.Select(f => f.RuleCode).ShouldBe(["B_MAX", "Z_MAX", "A_VAR"]);
    }
}
