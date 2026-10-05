using RegReturns.Domain.Common;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Templates;

namespace RegReturns.UnitTests.Domain;

public sealed class TemplateVersionTests
{
    private static readonly Guid ReturnTypeId = Guid.CreateVersion7();

    private static TemplateVersion Draft(int version = 1, DateOnly? effectiveFrom = null) =>
        TemplateVersion.CreateDraft(ReturnTypeId, version, effectiveFrom ?? new DateOnly(2026, 1, 1));

    // TOTAL = A + B (error), A >= 0 (error), NOTE is free text.
    private static TemplateVersion SampleDraft()
    {
        var template = Draft();
        template.AddField("A", "A", "S", FieldDataType.Amount).IsSuccess.ShouldBeTrue();
        template.AddField("B", "B", "S", FieldDataType.Amount).IsSuccess.ShouldBeTrue();
        template.AddField("TOTAL", "Total", "S", FieldDataType.Amount).IsSuccess.ShouldBeTrue();
        template.AddField("NOTE", "Note", "Other", FieldDataType.Text).IsSuccess.ShouldBeTrue();
        template.AddRule(ValidationRule.CrossField(
            "SUM", "TOTAL", Severity.Error, "[TOTAL]", ComparisonOperator.Equal, "[A] + [B]", 0m, "Total must equal A + B.")).IsSuccess.ShouldBeTrue();
        template.AddRule(ValidationRule.Range("A_MIN", "A", Severity.Error, 0m, null, "A cannot be negative.")).IsSuccess.ShouldBeTrue();
        return template;
    }

    private static TemplateVersion Published(int version, DateOnly effectiveFrom)
    {
        var template = Draft(version, effectiveFrom);
        template.AddField("A", "A", "S", FieldDataType.Amount);
        template.Publish().IsSuccess.ShouldBeTrue();
        return template;
    }

    [Fact]
    public void Fields_get_sequential_display_order()
    {
        var template = Draft();
        template.AddField("A", "A", "S", FieldDataType.Amount);
        template.AddField("B", "B", "S", FieldDataType.WholeNumber, precision: 3);

        template.Fields.Select(f => f.DisplayOrder).ShouldBe([1, 2]);
    }

    [Fact]
    public void Whole_numbers_and_non_numeric_fields_have_no_decimal_places()
    {
        var template = Draft();
        template.AddField("W", "W", "S", FieldDataType.WholeNumber, precision: 3);
        template.AddField("T", "T", "S", FieldDataType.Text, precision: 3);

        template.FindField("W")!.Precision.ShouldBe(0);
        template.FindField("T")!.Precision.ShouldBe(0);
    }

    [Fact]
    public void Field_text_is_trimmed()
    {
        var field = Draft().AddField(new FieldDefinition(" A ", " Label ", " Section ", FieldDataType.Amount, " VLD m ")).Value;

        field.ToDefinition().ShouldBe(new FieldDefinition("A", "Label", "Section", FieldDataType.Amount, "VLD m"));
    }

    [Fact]
    public void Duplicate_field_codes_are_rejected()
    {
        var template = Draft();
        template.AddField("A", "A", "S", FieldDataType.Amount);

        template.AddField("A", "Again", "S", FieldDataType.Amount).Error.ShouldBe(TemplateErrors.DuplicateFieldCode);
    }

    [Theory]
    [InlineData("lower")]
    [InlineData("1ABC")]
    [InlineData("WITH SPACE")]
    [InlineData("")]
    public void Field_codes_must_be_upper_snake_case(string code)
    {
        Draft().AddField(code, "Label", "S", FieldDataType.Amount).Error!.Code.ShouldBe(TemplateErrors.InvalidField.Code);
    }

    [Theory]
    [InlineData("", "S", "", 2, "label")]
    [InlineData("L", " ", "", 2, "section")]
    [InlineData("L", "S", "123456789012345678901", 2, "unit")]
    [InlineData("L", "S", "", 5, "Decimal places")]
    [InlineData("L", "S", "", -1, "Decimal places")]
    public void Invalid_field_definitions_explain_the_problem(string label, string section, string unit, int precision, string expected)
    {
        var result = Draft().AddField(new FieldDefinition("A", label, section, FieldDataType.Amount, unit, precision));

        result.Error!.Code.ShouldBe(TemplateErrors.InvalidField.Code);
        result.Error.Message.ShouldContain(expected);
    }

    [Fact]
    public void Undefined_data_types_are_rejected()
    {
        Draft().AddField(new FieldDefinition("A", "A", "S", (FieldDataType)99)).Error!.Code.ShouldBe(TemplateErrors.InvalidField.Code);
    }

    [Fact]
    public void Rules_must_target_an_existing_field()
    {
        var template = SampleDraft();

        template.AddRule(ValidationRule.Required("R1", "MISSING", "m")).Error!.Code.ShouldBe(TemplateErrors.UnknownField.Code);
    }

    [Fact]
    public void Rule_codes_are_unique_within_a_template()
    {
        var template = SampleDraft();

        template.AddRule(ValidationRule.DataType("A_MIN", "B", "m")).Error.ShouldBe(TemplateErrors.DuplicateRuleCode);
    }

    [Fact]
    public void Cross_field_expressions_may_only_name_fields_in_the_template()
    {
        var rule = ValidationRule.CrossField("X", "TOTAL", Severity.Error, "[TOTAL]", ComparisonOperator.Equal, "[A] + [C]", 0m, "m");

        var result = SampleDraft().AddRule(rule);

        result.Error!.Code.ShouldBe(TemplateErrors.UnknownField.Code);
        result.Error.Message.ShouldContain("'C'");
    }

    [Fact]
    public void Cross_field_expressions_may_only_name_numeric_fields()
    {
        var rule = ValidationRule.CrossField("X", "TOTAL", Severity.Error, "[TOTAL]", ComparisonOperator.Equal, "[A] + [NOTE]", 0m, "m");

        SampleDraft().AddRule(rule).Error!.Code.ShouldBe(TemplateErrors.RuleNeedsNumericField.Code);
    }

    [Theory]
    [InlineData(RuleType.Range)]
    [InlineData(RuleType.Variance)]
    public void Numeric_rules_cannot_target_text_fields(RuleType type)
    {
        var rule = ValidationRule.Create(new RuleDefinition(
            "X", type, Severity.Warning, "NOTE", "m", MinValue: 0m, ThresholdPercent: 10m, VarianceBasis: VarianceBasis.PreviousPeriod)).Value;

        SampleDraft().AddRule(rule).Error!.Code.ShouldBe(TemplateErrors.RuleNeedsNumericField.Code);
    }

    [Fact]
    public void Required_and_data_type_rules_work_on_any_field()
    {
        var template = SampleDraft();

        template.AddRule(ValidationRule.Required("NOTE_REQ", "NOTE", "m")).IsSuccess.ShouldBeTrue();
        template.AddRule(ValidationRule.DataType("NOTE_TYPE", "NOTE", "m")).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Fields_can_be_updated_except_for_their_code()
    {
        var template = SampleDraft();

        template.UpdateField(new FieldDefinition("B", "Level B", "Totals", FieldDataType.Amount, "VLD bn", 4)).IsSuccess.ShouldBeTrue();

        template.FindField("B")!.ToDefinition().ShouldBe(new FieldDefinition("B", "Level B", "Totals", FieldDataType.Amount, "VLD bn", 4));
    }

    [Fact]
    public void Updating_an_unknown_field_fails()
    {
        SampleDraft().UpdateField(new FieldDefinition("NOPE", "L", "S", FieldDataType.Amount)).Error!.Code
            .ShouldBe(TemplateErrors.UnknownField.Code);
    }

    [Fact]
    public void A_field_used_by_a_cross_field_rule_must_stay_numeric()
    {
        SampleDraft().UpdateField(new FieldDefinition("B", "B", "S", FieldDataType.Text)).Error!.Code
            .ShouldBe(TemplateErrors.FieldInUse.Code);
    }

    [Fact]
    public void A_field_used_by_a_range_rule_must_stay_numeric()
    {
        SampleDraft().UpdateField(new FieldDefinition("A", "A", "S", FieldDataType.Date)).Error!.Code
            .ShouldBe(TemplateErrors.FieldInUse.Code);
    }

    [Fact]
    public void A_used_field_may_change_between_numeric_types()
    {
        SampleDraft().UpdateField(new FieldDefinition("A", "A", "S", FieldDataType.WholeNumber)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Unused_fields_can_be_removed_and_the_order_closes_up()
    {
        var template = SampleDraft();

        template.RemoveField("TOTAL").Error!.Code.ShouldBe(TemplateErrors.FieldInUse.Code);
        template.RemoveRule("SUM").IsSuccess.ShouldBeTrue();
        template.RemoveField("TOTAL").IsSuccess.ShouldBeTrue();

        template.Fields.OrderBy(f => f.DisplayOrder).Select(f => (f.Code, f.DisplayOrder)).ShouldBe([("A", 1), ("B", 2), ("NOTE", 3)]);
    }

    [Fact]
    public void Removing_a_field_in_use_names_the_rules_using_it()
    {
        SampleDraft().RemoveField("A").Error!.Message.ShouldContain("SUM, A_MIN");
    }

    [Theory]
    [InlineData("NOTE", -1, new[] { "A", "B", "NOTE", "TOTAL" })]
    [InlineData("A", 1, new[] { "B", "A", "TOTAL", "NOTE" })]
    [InlineData("A", -5, new[] { "A", "B", "TOTAL", "NOTE" })]
    [InlineData("A", 10, new[] { "B", "TOTAL", "NOTE", "A" })]
    public void Fields_move_up_and_down_and_stop_at_the_ends(string code, int offset, string[] expected)
    {
        var template = SampleDraft();

        template.MoveField(code, offset).IsSuccess.ShouldBeTrue();

        template.Fields.OrderBy(f => f.DisplayOrder).Select(f => f.Code).ShouldBe(expected);
        template.Fields.Select(f => f.DisplayOrder).Order().ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public void Rules_can_be_switched_off_and_on()
    {
        var template = SampleDraft();

        template.SetRuleActive("SUM", false).IsSuccess.ShouldBeTrue();
        template.FindRule("SUM")!.IsActive.ShouldBeFalse();
        template.SetRuleActive("SUM", true).IsSuccess.ShouldBeTrue();
        template.FindRule("SUM")!.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Changing_an_unknown_rule_fails()
    {
        var template = SampleDraft();

        template.RemoveRule("NOPE").Error.ShouldBe(TemplateErrors.UnknownRule);
        template.SetRuleActive("NOPE", false).Error.ShouldBe(TemplateErrors.UnknownRule);
    }

    [Fact]
    public void The_effective_date_of_a_draft_can_change()
    {
        var template = SampleDraft();

        template.ChangeEffectiveFrom(new DateOnly(2027, 1, 1)).IsSuccess.ShouldBeTrue();

        template.EffectiveFrom.ShouldBe(new DateOnly(2027, 1, 1));
    }

    [Fact]
    public void Published_templates_are_immutable()
    {
        var template = SampleDraft();
        template.Publish().IsSuccess.ShouldBeTrue();

        template.AddField("C", "C", "S", FieldDataType.Amount).Error.ShouldBe(TemplateErrors.NotDraft);
        template.UpdateField(new FieldDefinition("A", "Changed", "S", FieldDataType.Amount)).Error.ShouldBe(TemplateErrors.NotDraft);
        template.RemoveField("NOTE").Error.ShouldBe(TemplateErrors.NotDraft);
        template.MoveField("NOTE", -1).Error.ShouldBe(TemplateErrors.NotDraft);
        template.AddRule(ValidationRule.Required("R", "A", "m")).Error.ShouldBe(TemplateErrors.NotDraft);
        template.RemoveRule("SUM").Error.ShouldBe(TemplateErrors.NotDraft);
        template.SetRuleActive("SUM", false).Error.ShouldBe(TemplateErrors.NotDraft);
        template.ChangeEffectiveFrom(new DateOnly(2027, 1, 1)).Error.ShouldBe(TemplateErrors.NotDraft);
        template.Publish().Error.ShouldBe(TemplateErrors.NotDraft);
    }

    [Fact]
    public void Templates_need_fields_before_publishing()
    {
        Draft().Publish().Error.ShouldBe(TemplateErrors.NoFields);
    }

    [Fact]
    public void Only_published_templates_can_be_retired()
    {
        var template = SampleDraft();
        template.Retire().Error.ShouldBe(TemplateErrors.NotPublished);

        template.Publish();
        template.Retire().IsSuccess.ShouldBeTrue();

        template.Status.ShouldBe(TemplateStatus.Retired);
        template.Retire().Error.ShouldBe(TemplateErrors.NotPublished);
    }

    [Fact]
    public void A_copy_is_a_draft_with_the_same_fields_and_rules_and_new_ids()
    {
        var source = SampleDraft();
        source.SetRuleActive("A_MIN", false);
        source.MoveField("NOTE", -3);
        source.Publish();

        var copy = source.CopyAsDraft(2, new DateOnly(2027, 1, 1));

        copy.Status.ShouldBe(TemplateStatus.Draft);
        copy.Version.ShouldBe(2);
        copy.ReturnTypeId.ShouldBe(source.ReturnTypeId);
        copy.EffectiveFrom.ShouldBe(new DateOnly(2027, 1, 1));
        copy.Fields.Select(f => f.ToDefinition()).ShouldBe(source.Fields.Select(f => f.ToDefinition()), ignoreOrder: true);
        copy.Fields.OrderBy(f => f.DisplayOrder).Select(f => f.Code).ShouldBe(["NOTE", "A", "B", "TOTAL"]);
        copy.Rules.Select(r => r.ToDefinition()).ShouldBe(source.Rules.Select(r => r.ToDefinition()));
        copy.FindRule("A_MIN")!.IsActive.ShouldBeFalse();
        copy.Fields.Select(f => f.Id).Intersect(source.Fields.Select(f => f.Id)).ShouldBeEmpty();
        copy.Rules.Select(r => r.Id).Intersect(source.Rules.Select(r => r.Id)).ShouldBeEmpty();
    }

    [Fact]
    public void Changing_a_copy_leaves_the_source_alone()
    {
        var source = SampleDraft();
        source.Publish();
        var copy = source.CopyAsDraft(2, new DateOnly(2027, 1, 1));

        copy.UpdateField(new FieldDefinition("A", "Changed", "S", FieldDataType.Amount));
        copy.SetRuleActive("SUM", false);

        source.FindField("A")!.Label.ShouldBe("A");
        source.FindRule("SUM")!.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void A_period_uses_the_latest_published_version_effective_by_its_start()
    {
        var v1 = Published(1, new DateOnly(2025, 1, 1));
        var v2 = Published(2, new DateOnly(2026, 7, 1));
        var draft = Draft(3, new DateOnly(2026, 1, 1));
        draft.AddField("A", "A", "S", FieldDataType.Amount);

        TemplateVersion.SelectFor([v1, v2, draft], ReportingPeriod.Monthly(2026, 6)).ShouldBe(v1);
        TemplateVersion.SelectFor([v1, v2, draft], ReportingPeriod.Monthly(2026, 7)).ShouldBe(v2);
        TemplateVersion.SelectFor([v1, v2, draft], ReportingPeriod.Quarterly(2026, 3)).ShouldBe(v2);
    }

    [Fact]
    public void Retired_versions_are_never_selected()
    {
        var v1 = Published(1, new DateOnly(2025, 1, 1));
        var v2 = Published(2, new DateOnly(2026, 1, 1));
        v2.Retire();

        TemplateVersion.SelectFor([v1, v2], ReportingPeriod.Monthly(2026, 6)).ShouldBe(v1);
    }

    [Fact]
    public void The_higher_version_wins_when_two_share_an_effective_date()
    {
        var v1 = Published(1, new DateOnly(2026, 1, 1));
        var v2 = Published(2, new DateOnly(2026, 1, 1));

        TemplateVersion.SelectFor([v2, v1], ReportingPeriod.Monthly(2026, 1)).ShouldBe(v2);
    }

    [Fact]
    public void No_version_is_selected_before_the_first_effective_date()
    {
        TemplateVersion.SelectFor([Published(1, new DateOnly(2026, 1, 1))], ReportingPeriod.Monthly(2025, 12)).ShouldBeNull();
    }

    [Fact]
    public void Versions_start_at_one()
    {
        Should.Throw<DomainException>(() => Draft(0));
    }
}
