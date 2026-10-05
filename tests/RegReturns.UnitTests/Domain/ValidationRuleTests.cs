using RegReturns.Domain.Common;
using RegReturns.Domain.Templates;

namespace RegReturns.UnitTests.Domain;

public sealed class ValidationRuleTests
{
    private static RuleDefinition Range(decimal? min, decimal? max) =>
        new("R", RuleType.Range, Severity.Error, "A", "m", MinValue: min, MaxValue: max);

    private static RuleDefinition CrossField(string? left, string? right, ComparisonOperator? op = ComparisonOperator.Equal, decimal? tolerance = null) =>
        new("R", RuleType.CrossField, Severity.Error, "A", "m", LeftExpression: left, Operator: op, RightExpression: right, Tolerance: tolerance);

    private static RuleDefinition Variance(decimal? threshold, VarianceBasis? basis = VarianceBasis.PreviousPeriod) =>
        new("R", RuleType.Variance, Severity.Warning, "A", "m", ThresholdPercent: threshold, VarianceBasis: basis);

    private static void ShouldBeInvalid(RuleDefinition definition, string expected)
    {
        var result = ValidationRule.Create(definition);

        result.Error!.Code.ShouldBe(TemplateErrors.InvalidRule.Code);
        result.Error.Message.ShouldContain(expected);
    }

    [Fact]
    public void A_valid_definition_creates_an_active_rule_with_trimmed_text()
    {
        var rule = ValidationRule.Create(new RuleDefinition(" R1 ", RuleType.Required, Severity.Error, " A ", " A is required. ")).Value;

        rule.Code.ShouldBe("R1");
        rule.TargetFieldCode.ShouldBe("A");
        rule.Message.ShouldBe("A is required.");
        rule.IsActive.ShouldBeTrue();
    }

    [Theory]
    [InlineData("r1")]
    [InlineData("")]
    [InlineData("R 1")]
    public void Rule_codes_must_be_upper_snake_case(string code)
    {
        ShouldBeInvalid(new RuleDefinition(code, RuleType.Required, Severity.Error, "A", "m"), "rule code");
    }

    [Fact]
    public void A_target_field_is_required()
    {
        ShouldBeInvalid(new RuleDefinition("R", RuleType.Required, Severity.Error, "", "m"), "field");
    }

    [Fact]
    public void A_message_is_required()
    {
        ShouldBeInvalid(new RuleDefinition("R", RuleType.Required, Severity.Error, "A", "  "), "message");
    }

    [Fact]
    public void Messages_longer_than_the_column_are_rejected()
    {
        ShouldBeInvalid(new RuleDefinition("R", RuleType.Required, Severity.Error, "A", new string('m', 501)), "message");
    }

    [Theory]
    [InlineData(RuleType.Required)]
    [InlineData(RuleType.DataType)]
    public void Required_and_data_type_rules_are_always_errors(RuleType type)
    {
        ShouldBeInvalid(new RuleDefinition("R", type, Severity.Warning, "A", "m"), "always errors");
    }

    [Fact]
    public void Undefined_rule_types_are_rejected()
    {
        ShouldBeInvalid(new RuleDefinition("R", (RuleType)99, Severity.Error, "A", "m"), "rule type");
    }

    [Fact]
    public void A_range_needs_a_limit()
    {
        ShouldBeInvalid(Range(null, null), "minimum, a maximum or both");
    }

    [Fact]
    public void A_range_minimum_cannot_exceed_its_maximum()
    {
        ShouldBeInvalid(Range(5m, 1m), "cannot be greater");
    }

    [Theory]
    [InlineData(0.00001)]
    [InlineData(1e16)]
    public void Range_limits_must_fit_the_column(double limit)
    {
        ShouldBeInvalid(Range((decimal)limit, null), "decimal places");
    }

    [Theory]
    [InlineData(1.0, null)]
    [InlineData(null, 100.0)]
    [InlineData(1.0, 1.0)]
    public void A_range_may_be_open_on_one_side_or_a_single_value(double? min, double? max)
    {
        var rule = ValidationRule.Create(Range((decimal?)min, (decimal?)max)).Value;

        rule.MinValue.ShouldBe((decimal?)min);
        rule.MaxValue.ShouldBe((decimal?)max);
    }

    [Fact]
    public void A_cross_field_rule_keeps_its_parsed_expressions_and_defaults_the_tolerance_to_zero()
    {
        var rule = ValidationRule.Create(CrossField(" [TOTAL] ", "[A] + [B]")).Value;

        rule.LeftExpression.ShouldBe("[TOTAL]");
        rule.RightExpression.ShouldBe("[A] + [B]");
        rule.Operator.ShouldBe(ComparisonOperator.Equal);
        rule.Tolerance.ShouldBe(0m);
        rule.GetReferencedFieldCodes().ShouldBe(["A", "B", "TOTAL"], ignoreOrder: true);
    }

    [Fact]
    public void A_bad_left_expression_says_which_side_is_wrong()
    {
        var result = ValidationRule.Create(CrossField("[A] +", "[B]"));

        result.Error!.Code.ShouldBe(TemplateErrors.InvalidExpression.Code);
        result.Error.Message.ShouldStartWith("Left side:");
    }

    [Fact]
    public void A_bad_right_expression_says_which_side_is_wrong()
    {
        ValidationRule.Create(CrossField("[A]", null)).Error!.Message.ShouldStartWith("Right side:");
    }

    [Fact]
    public void A_cross_field_rule_needs_a_comparison()
    {
        ShouldBeInvalid(CrossField("[A]", "[B]", op: null), "compare");
    }

    [Fact]
    public void A_negative_tolerance_is_rejected()
    {
        ShouldBeInvalid(CrossField("[A]", "[B]", tolerance: -0.01m), "tolerance");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(null)]
    public void A_variance_threshold_must_be_positive(double? threshold)
    {
        ShouldBeInvalid(Variance((decimal?)threshold), "positive percentage");
    }

    [Fact]
    public void A_variance_rule_needs_a_basis()
    {
        ShouldBeInvalid(Variance(10m, basis: null), "earlier period");
    }

    [Fact]
    public void The_shortcut_factories_throw_on_programming_errors()
    {
        Should.Throw<DomainException>(() => ValidationRule.Range("R", "A", Severity.Error, null, null, "m"));
        Should.Throw<DomainException>(() => ValidationRule.Variance("R", "A", Severity.Warning, 0m, VarianceBasis.PreviousPeriod, "m"));
        Should.Throw<DomainException>(() =>
            ValidationRule.CrossField("R", "A", Severity.Error, "[A]", ComparisonOperator.Equal, "[B]", -1m, "m"));
    }

    [Fact]
    public void A_definition_round_trips()
    {
        var definition = new RuleDefinition(
            "R", RuleType.CrossField, Severity.Warning, "A", "m",
            LeftExpression: "[A]", Operator: ComparisonOperator.LessThanOrEqual, RightExpression: "0.15 * [B]", Tolerance: 0.5m);

        ValidationRule.Create(definition).Value.ToDefinition().ShouldBe(definition);
    }
}
