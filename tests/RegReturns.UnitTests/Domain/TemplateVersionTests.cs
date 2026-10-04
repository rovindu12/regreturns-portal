using RegReturns.Domain.Common;
using RegReturns.Domain.Templates;

namespace RegReturns.UnitTests.Domain;

public sealed class TemplateVersionTests
{
    private static TemplateVersion Draft() => TemplateVersion.CreateDraft(Guid.CreateVersion7(), 1, new DateOnly(2026, 1, 1));

    [Fact]
    public void Fields_get_sequential_display_order()
    {
        var template = Draft();
        template.AddField("A", "A", "S", FieldDataType.Amount);
        template.AddField("B", "B", "S", FieldDataType.WholeNumber, precision: 3);

        template.Fields.Select(f => f.DisplayOrder).ShouldBe([1, 2]);
        template.FindField("B")!.Precision.ShouldBe(0);
    }

    [Fact]
    public void Duplicate_field_codes_are_rejected()
    {
        var template = Draft();
        template.AddField("A", "A", "S", FieldDataType.Amount);

        template.AddField("A", "Again", "S", FieldDataType.Amount).Error.ShouldBe(TemplateErrors.DuplicateFieldCode);
    }

    [Fact]
    public void Rules_must_target_an_existing_field_and_have_a_unique_code()
    {
        var template = Draft();
        template.AddField("A", "A", "S", FieldDataType.Amount);

        template.AddRule(ValidationRule.Required("R1", "MISSING", "m")).Error.ShouldBe(TemplateErrors.UnknownField);
        template.AddRule(ValidationRule.Required("R1", "A", "m")).IsSuccess.ShouldBeTrue();
        template.AddRule(ValidationRule.DataType("R1", "A", "m")).Error.ShouldBe(TemplateErrors.DuplicateRuleCode);
    }

    [Fact]
    public void Published_templates_are_immutable()
    {
        var template = Draft();
        template.AddField("A", "A", "S", FieldDataType.Amount);
        template.Publish().IsSuccess.ShouldBeTrue();

        template.AddField("B", "B", "S", FieldDataType.Amount).Error.ShouldBe(TemplateErrors.NotDraft);
        template.AddRule(ValidationRule.Required("R", "A", "m")).Error.ShouldBe(TemplateErrors.NotDraft);
        template.Publish().Error.ShouldBe(TemplateErrors.NotDraft);
    }

    [Fact]
    public void Templates_need_fields_before_publishing()
    {
        Draft().Publish().Error.ShouldBe(TemplateErrors.NoFields);
    }

    [Theory]
    [InlineData("lower")]
    [InlineData("1ABC")]
    [InlineData("WITH SPACE")]
    public void Field_codes_must_be_upper_snake_case(string code)
    {
        Should.Throw<DomainException>(() => Draft().AddField(code, "Label", "S", FieldDataType.Amount));
    }

    [Fact]
    public void Rule_factories_reject_inconsistent_parameters()
    {
        Should.Throw<DomainException>(() => ValidationRule.Range("R", "A", Severity.Error, null, null, "m"));
        Should.Throw<DomainException>(() => ValidationRule.Range("R", "A", Severity.Error, 5m, 1m, "m"));
        Should.Throw<DomainException>(() => ValidationRule.Variance("R", "A", Severity.Warning, 0m, VarianceBasis.PreviousPeriod, "m"));
        Should.Throw<DomainException>(() =>
            ValidationRule.CrossField("R", "A", Severity.Error, "[A]", ComparisonOperator.Equal, "[B]", -1m, "m"));
    }

    [Fact]
    public void Cross_field_rule_keeps_its_expressions()
    {
        var rule = ValidationRule.CrossField(
            "SUM", "TOTAL", Severity.Error, "[TOTAL]", ComparisonOperator.Equal, "[A] + [B]", 0.05m, "Total must equal A + B.");

        rule.RuleType.ShouldBe(RuleType.CrossField);
        rule.LeftExpression.ShouldBe("[TOTAL]");
        rule.RightExpression.ShouldBe("[A] + [B]");
        rule.Tolerance.ShouldBe(0.05m);
    }
}
