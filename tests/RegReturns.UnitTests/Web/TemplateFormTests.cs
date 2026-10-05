using System.Globalization;

using RegReturns.Domain.Templates;
using RegReturns.Web.Models.Templates;

namespace RegReturns.UnitTests.Web;

public sealed class TemplateFormTests
{
    [Fact]
    public void Decimal_with_a_dot_is_read_whatever_the_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            FormInput.OptionalDecimal(" 1500.25 ", "minimum").Value.ShouldBe(1500.25m);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("1,000")]
    [InlineData("12%")]
    [InlineData("1e3")]
    [InlineData("99999999999999999999999999999999")]
    public void Decimal_that_is_not_plainly_written_is_refused_with_the_input_error(string text)
    {
        var result = FormInput.OptionalDecimal(text, "minimum");

        result.Error.ShouldNotBeNull().Code.ShouldBe(FormErrors.InvalidInput.Code);
        result.Error.Message.ShouldStartWith("The minimum must be a number written with a dot");
    }

    [Fact]
    public void Blank_decimal_has_no_value()
    {
        FormInput.OptionalDecimal("  ", "maximum").Value.ShouldBeNull();
    }

    [Theory]
    [InlineData("1")]
    [InlineData("Amount, Text")]
    [InlineData("amount")]
    public void Choice_accepts_member_names_only(string text)
    {
        FormInput.Choice<FieldDataType>(text, "Choose a data type.").Error.ShouldNotBeNull().Message.ShouldBe("Choose a data type.");
    }

    [Fact]
    public void Date_must_be_written_as_a_date_input_posts_it()
    {
        FormInput.Date("2026-11-01", "effective-from date").Value.ShouldBe(new DateOnly(2026, 11, 1));
        FormInput.Date("01/11/2026", "effective-from date").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Field_form_trims_and_upper_cases_the_code()
    {
        var form = new FieldForm
        {
            Code = " total_buffer ",
            Label = "Total buffer",
            Section = "Buffers",
            DataType = nameof(FieldDataType.Amount),
            Precision = "2",
        };

        form.ToDefinition().Value.ShouldBe(new FieldDefinition("TOTAL_BUFFER", "Total buffer", "Buffers", FieldDataType.Amount, string.Empty, 2));
    }

    [Fact]
    public void Field_form_refuses_decimal_places_that_are_not_a_whole_number()
    {
        var form = new FieldForm { Code = "X", Label = "X", Section = "S", DataType = nameof(FieldDataType.Amount), Precision = "1.5" };

        form.ToDefinition().Error.ShouldNotBeNull().Code.ShouldBe(FormErrors.InvalidInput.Code);
    }

    [Fact]
    public void Rule_form_reads_only_the_parameters_of_the_chosen_rule_type()
    {
        var form = new RuleForm
        {
            Code = "MLR_LCR_FLOOR",
            RuleType = nameof(RuleType.Range),
            Severity = nameof(Severity.Warning),
            TargetFieldCode = "LCR",
            Message = "LCR is low.",
            MinValue = "100",
            ThresholdPercent = "not a number",
            Tolerance = "junk",
        };

        var rule = form.ToDefinition().Value;

        rule.MinValue.ShouldBe(100m);
        rule.MaxValue.ShouldBeNull();
        rule.ThresholdPercent.ShouldBeNull();
        rule.Tolerance.ShouldBeNull();
    }

    [Fact]
    public void Cross_field_form_keeps_the_expressions_for_the_domain_to_parse()
    {
        var form = new RuleForm
        {
            Code = "MLR_HQLA_SUM",
            RuleType = nameof(RuleType.CrossField),
            Severity = nameof(Severity.Error),
            TargetFieldCode = "TOTAL_HQLA",
            Message = "Sum.",
            LeftExpression = "[TOTAL_HQLA]",
            Operator = nameof(ComparisonOperator.Equal),
            RightExpression = "[L1] +",
            Tolerance = "0.05",
        };

        var rule = form.ToDefinition().Value;

        rule.RightExpression.ShouldBe("[L1] +");
        rule.Operator.ShouldBe(ComparisonOperator.Equal);
        rule.Tolerance.ShouldBe(0.05m);
    }

    [Theory]
    [InlineData(RuleType.Range, "Between 0 and 1000")]
    [InlineData(RuleType.CrossField, "[L2B_HQLA] ≤ 0.15 * [TOTAL_HQLA] (tolerance 0.05)")]
    [InlineData(RuleType.Variance, "Change of at most 25% against the previous period")]
    public void Rule_parameters_are_summarised_in_one_line(RuleType ruleType, string expected)
    {
        var rule = new RuleDefinition(
            "R", ruleType, Severity.Warning, "F", "Message", MinValue: 0.0000m, MaxValue: 1000.0000m, LeftExpression: "[L2B_HQLA]",
            Operator: ComparisonOperator.LessThanOrEqual, RightExpression: "0.15 * [TOTAL_HQLA]", Tolerance: 0.0500m,
            ThresholdPercent: 25.0000m, VarianceBasis: VarianceBasis.PreviousPeriod);

        TemplateDisplay.Parameters(rule).ShouldBe(expected);
    }
}
