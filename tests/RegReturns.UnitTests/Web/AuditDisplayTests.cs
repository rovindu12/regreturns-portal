using RegReturns.Domain.Auditing;
using RegReturns.Web.Models.Audit;

namespace RegReturns.UnitTests.Web;

public sealed class AuditDisplayTests
{
    public static TheoryData<AuditAction> Actions() => [.. Enum.GetValues<AuditAction>()];

    [Theory]
    [MemberData(nameof(Actions))]
    public void Every_action_has_a_label_in_words(AuditAction action)
    {
        // An action without a label would show its enum name, such as "DemoReset".
        AuditDisplay.ActionLabel(action).Skip(1).ShouldNotContain(c => char.IsUpper(c));
    }

    [Fact]
    public void A_demo_reset_is_labelled_for_the_auditor()
    {
        AuditDisplay.ActionLabel(AuditAction.DemoReset).ShouldBe("Demo reset");
    }
}
