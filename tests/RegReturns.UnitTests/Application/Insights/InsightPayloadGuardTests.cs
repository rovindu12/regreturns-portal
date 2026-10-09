using System.Text.Json.Nodes;

using RegReturns.Application.Insights;
using RegReturns.Domain.Templates;
using RegReturns.UnitTests.TestSupport;

namespace RegReturns.UnitTests.Application.Insights;

public sealed class InsightPayloadGuardTests
{
    private readonly InsightWorld _world = new();

    [Fact]
    public void A_built_payload_passes()
    {
        InsightPayloadGuard.Check(_world.Payload(), _world.ReturnType, _world.Template).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Text_the_template_does_not_hold_is_rejected_with_its_path()
    {
        var payload = JsonNode.Parse(_world.Payload())!;
        payload["returnType"]!["name"] = InsightWorld.BankName;

        var result = InsightPayloadGuard.Check(payload.ToJsonString(), _world.ReturnType, _world.Template);

        result.Error.ShouldNotBeNull().Is(InsightPayloadGuard.Rejected).ShouldBeTrue();
        result.Error.Message.ShouldContain("$.returnType.name");
        result.Error.Message.ShouldNotContain(InsightWorld.BankName);
    }

    [Fact]
    public void A_property_added_to_the_payload_is_checked_too()
    {
        var payload = JsonNode.Parse(_world.Payload())!;
        payload["fields"]![0]!["comment"] = InsightWorld.Justification;

        var result = InsightPayloadGuard.Check(payload.ToJsonString(), _world.ReturnType, _world.Template);

        result.Error.ShouldNotBeNull().Message.ShouldContain("$.fields[0].comment");
    }

    [Theory]
    [InlineData("2026-02")]
    [InlineData("2026-12")]
    [InlineData("2026-Q4")]
    public void Period_labels_pass(string label)
    {
        var payload = JsonNode.Parse(_world.Payload())!;
        payload["period"] = label;

        InsightPayloadGuard.Check(payload.ToJsonString(), _world.ReturnType, _world.Template).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("2026-02-28")]
    [InlineData("2026-13")]
    [InlineData("2026-Q5")]
    [InlineData("2026-02\n")]
    public void Other_dates_and_almost_period_labels_are_rejected(string label)
    {
        var payload = JsonNode.Parse(_world.Payload())!;
        payload["period"] = label;

        InsightPayloadGuard.Check(payload.ToJsonString(), _world.ReturnType, _world.Template).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Template_text_holding_an_email_address_is_never_sent()
    {
        var template = TemplateVersion.CreateDraft(_world.ReturnType.Id, 2, new DateOnly(2026, 1, 1));
        foreach (var field in _world.Template.Fields.OrderBy(f => f.DisplayOrder))
        {
            template.AddField(field.Code, field.Label, field.Section, field.DataType, field.Unit, field.Precision).IsSuccess.ShouldBeTrue();
        }

        var rule = ValidationRule.Range("DEP_MIN", "DEPOSITS", Severity.Warning, 0m, null, "Negative deposits: tell risk.desk@valoria.example.");
        template.AddRule(rule).IsSuccess.ShouldBeTrue();
        template.Publish().IsSuccess.ShouldBeTrue();
        var payload = JsonNode.Parse(_world.Payload())!;
        payload["findings"]![0]!["rule"] = rule.Message;

        var result = InsightPayloadGuard.Check(payload.ToJsonString(), _world.ReturnType, template);

        result.Error.ShouldNotBeNull().Message.ShouldContain("$.findings[0].rule");
    }
}
