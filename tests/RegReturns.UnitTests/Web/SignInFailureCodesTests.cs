using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using RegReturns.Web.Identity;
using RegReturns.Web.Models;

namespace RegReturns.UnitTests.Web;

public sealed class SignInFailureCodesTests
{
    [Fact]
    public void Known_OAuth_error_from_WSO2_is_kept_as_the_code()
    {
        var failure = new OpenIdConnectProtocolException("Message contains error: 'access_denied'.");
        failure.Data["error"] = "access_denied";

        SignInFailureCodes.From(failure).ShouldBe(SignInFailureCodes.AccessDenied);
    }

    [Fact]
    public void Unknown_OAuth_error_is_not_passed_through()
    {
        var failure = new OpenIdConnectProtocolException("Message contains error.");
        failure.Data["error"] = "<script>alert(1)</script>";

        SignInFailureCodes.From(failure).ShouldBe(SignInFailureCodes.ProtocolError);
    }

    [Fact]
    public void Invalid_id_token_maps_to_a_short_code()
    {
        SignInFailureCodes.From(new SecurityTokenInvalidIssuerException("IDX10205")).ShouldBe(SignInFailureCodes.TokenInvalid);
    }

    [Fact]
    public void Failed_correlation_maps_to_an_expired_request()
    {
        SignInFailureCodes.From(new AuthenticationFailureException("Correlation failed.")).ShouldBe(SignInFailureCodes.RequestExpired);
    }

    [Fact]
    public void Portal_refusal_keeps_its_error_code()
    {
        SignInFailureCodes.From(new SignInRejectedException("User.UnknownInstitution")).ShouldBe("User.UnknownInstitution");
    }

    [Fact]
    public void Failure_page_ignores_a_reference_that_is_not_a_trace_id()
    {
        SignInFailedViewModel.Create("access_denied", "<b>not-a-trace</b>").ErrorReference.ShouldBeNull();
    }

    [Fact]
    public void Failure_page_shows_a_generic_message_for_unknown_reasons()
    {
        SignInFailedViewModel.Create("made-up", "4bf92f3577b34da6a3ce929d0e0e4736").Message.ShouldBe(SignInFailedViewModel.GenericMessage);
    }
}
