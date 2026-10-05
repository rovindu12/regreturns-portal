using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using RegReturns.Application.Identity;
using RegReturns.Web.Identity;

namespace RegReturns.UnitTests.Web;

public sealed class HttpCurrentUserTests
{
    private static HttpCurrentUser For(ClaimsIdentity? identity)
    {
        var accessor = new HttpContextAccessor();
        if (identity is not null)
        {
            accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        }

        return new HttpCurrentUser(accessor);
    }

    [Fact]
    public void A_signed_in_user_is_identified_by_the_subject_claim()
    {
        var user = For(new ClaimsIdentity([new Claim(ClaimNames.Subject, "wso2-subject")], "Cookies"));

        user.SubjectId.ShouldBe("wso2-subject");
    }

    [Fact]
    public void An_unauthenticated_identity_has_no_subject_even_with_the_claim()
    {
        var user = For(new ClaimsIdentity([new Claim(ClaimNames.Subject, "wso2-subject")]));

        user.SubjectId.ShouldBeNull();
    }

    [Fact]
    public void Outside_a_request_there_is_no_subject()
    {
        For(null).SubjectId.ShouldBeNull();
    }
}
