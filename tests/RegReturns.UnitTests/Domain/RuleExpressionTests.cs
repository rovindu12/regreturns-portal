using RegReturns.Domain.Templates;

namespace RegReturns.UnitTests.Domain;

public sealed class RuleExpressionTests
{
    private static readonly Dictionary<string, decimal?> Values = new(StringComparer.Ordinal)
    {
        ["A"] = 10m,
        ["B"] = 4m,
        ["C"] = 0m,
        ["NEG"] = -3m,
        ["BLANK"] = null,
    };

    private static decimal? Evaluate(string text) =>
        RuleExpression.Parse(text).Value.Evaluate(code => Values.GetValueOrDefault(code));

    [Theory]
    [InlineData("[A] + [B]", 14)]
    [InlineData("[A] - [B]", 6)]
    [InlineData("[A] * [B]", 40)]
    [InlineData("[A] / [B]", 2.5)]
    [InlineData("[A] - [B] * 2", 2)]
    [InlineData("([A] - [B]) * 2", 12)]
    [InlineData("-[A] + 1", -9)]
    [InlineData("--[A]", 10)]
    [InlineData("+[A]", 10)]
    [InlineData("[A] - -[B]", 14)]
    [InlineData("0.75 * [A]", 7.5)]
    [InlineData("[A] / [B] * 100", 250)]
    [InlineData("100", 100)]
    [InlineData(".5 + [A]", 10.5)]
    public void Arithmetic_follows_the_usual_precedence(string text, double expected)
    {
        Evaluate(text).ShouldBe((decimal)expected);
    }

    [Theory]
    [InlineData("Min([A], [B])", 4)]
    [InlineData("MAX([A], [B], 20)", 20)]
    [InlineData("min([A], 0.75 * [B])", 3)]
    [InlineData("Abs([NEG])", 3)]
    [InlineData("[A] - Min([B], 0.75 * [A])", 6)]
    public void Functions_are_case_insensitive_and_nest(string text, double expected)
    {
        Evaluate(text).ShouldBe((decimal)expected);
    }

    [Fact]
    public void Decimal_arithmetic_has_no_binary_rounding_errors()
    {
        RuleExpression.Parse("0.1 + 0.2").Value.Evaluate(_ => null).ShouldBe(0.3m);
    }

    [Theory]
    [InlineData("[BLANK] + [A]")]
    [InlineData("[UNKNOWN]")]
    [InlineData("Min([A], [BLANK])")]
    [InlineData("-[BLANK]")]
    public void A_missing_value_makes_the_whole_expression_have_no_value(string text)
    {
        Evaluate(text).ShouldBeNull();
    }

    [Fact]
    public void Division_by_zero_has_no_value()
    {
        Evaluate("[A] / [C]").ShouldBeNull();
    }

    [Fact]
    public void Overflow_has_no_value()
    {
        RuleExpression.Parse("79228162514264337593543950335 * 10").Value.Evaluate(_ => null).ShouldBeNull();
    }

    [Fact]
    public void Field_codes_are_collected_once_each()
    {
        RuleExpression.Parse("[A] + [B] - [A]").Value.FieldCodes.ShouldBe(["A", "B"], ignoreOrder: true);
    }

    [Fact]
    public void Text_is_trimmed()
    {
        RuleExpression.Parse("  [A] + 1  ").Value.Text.ShouldBe("[A] + 1");
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("[A] +", "ends too early")]
    [InlineData("[A] [B]", "Unexpected '['")]
    [InlineData("([A] + 1", "Expected ')'")]
    [InlineData("[A] + 1)", "Unexpected ')'")]
    [InlineData("[a]", "not a valid field code")]
    [InlineData("[]", "not a valid field code")]
    [InlineData("[A", "closing ']'")]
    [InlineData("[A B]", "not a valid field code")]
    [InlineData("1.2.3", "not a number")]
    [InlineData("Sqrt([A])", "Unknown function 'Sqrt'")]
    [InlineData("A + 1", "Unknown function 'A'")]
    [InlineData("Min([A])", "at least 2 arguments")]
    [InlineData("Abs([A], [B])", "exactly one argument")]
    [InlineData("[A] == [B]", "Unexpected '='")]
    [InlineData("[A] ^ 2", "Unexpected '^'")]
    public void Invalid_expressions_explain_what_is_wrong(string text, string expected)
    {
        var result = RuleExpression.Parse(text);

        result.Error!.Code.ShouldBe(TemplateErrors.InvalidExpression.Code);
        result.Error.Message.ShouldContain(expected);
    }

    [Fact]
    public void Syntax_errors_name_the_character_position()
    {
        RuleExpression.Parse("[A] + * [B]").Error!.Message.ShouldContain("(at character 7)");
    }

    [Fact]
    public void Expressions_longer_than_the_column_are_rejected()
    {
        var text = string.Join(" + ", Enumerable.Repeat("[A]", 200));

        RuleExpression.Parse(text).Error!.Message.ShouldContain($"at most {ValidationRule.ExpressionMaxLength} characters");
    }

    [Fact]
    public void Deep_nesting_is_rejected_instead_of_overflowing_the_stack()
    {
        var text = new string('(', 40) + "[A]" + new string(')', 40);

        RuleExpression.Parse(text).Error!.Message.ShouldContain("nested more than");
    }

    [Fact]
    public void Field_codes_longer_than_a_field_code_can_be_are_rejected()
    {
        var text = $"[{new string('A', TemplateField.CodeMaxLength + 1)}]";

        RuleExpression.Parse(text).Error!.Message.ShouldContain("not a valid field code");
    }
}
