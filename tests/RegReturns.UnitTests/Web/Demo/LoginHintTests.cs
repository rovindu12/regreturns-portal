using RegReturns.Web.Identity;

namespace RegReturns.UnitTests.Web.Demo;

public sealed class LoginHintTests
{
    [Theory]
    [InlineData("maker.hlb")]
    [InlineData("approver.mfa")]
    [InlineData("admin-demo")]
    [InlineData("auditor")]
    public void Plausible_user_names_are_sent_as_a_hint(string userName)
    {
        PortalOpenIdConnectEvents.IsLoginHint(userName).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Maker.HLB")]
    [InlineData(".hidden")]
    [InlineData("someone@bank.example")]
    [InlineData("maker hlb")]
    [InlineData("maker.hlb&prompt=none")]
    [InlineData("an-unreasonably-long-user-name-that-nobody-in-the-demo-directory-has")]
    public void Anything_else_is_left_out(string? value)
    {
        PortalOpenIdConnectEvents.IsLoginHint(value).ShouldBeFalse();
    }
}
