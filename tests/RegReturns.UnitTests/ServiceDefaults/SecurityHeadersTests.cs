using System.Buffers.Text;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using RegReturns.ServiceDefaults.Web;

namespace RegReturns.UnitTests.ServiceDefaults;

public sealed class SecurityHeadersTests
{
    [Fact]
    public void The_nonce_is_the_same_for_the_whole_request()
    {
        var context = new DefaultHttpContext();

        context.CspNonce().ShouldBe(context.CspNonce());
    }

    [Fact]
    public void Each_request_gets_its_own_128_bit_nonce()
    {
        var first = new DefaultHttpContext().CspNonce();
        var second = new DefaultHttpContext().CspNonce();

        first.ShouldNotBe(second);
        Base64Url.DecodeFromChars(first).Length.ShouldBe(16);
        first.ShouldMatch("^[A-Za-z0-9_-]+$", "URL-safe Base64 is not changed by HTML encoding.");
    }

    [Fact]
    public void The_fixed_headers_are_set_and_the_server_is_not_named()
    {
        var context = new DefaultHttpContext();
        context.Response.Headers.Server = "Kestrel";
        context.Response.Headers.XPoweredBy = "ASP.NET";

        SecurityHeaders.Apply(context, "default-src 'none'", "no-referrer");

        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy.ToString().ShouldBe("default-src 'none'");
        headers.XContentTypeOptions.ToString().ShouldBe("nosniff");
        headers.XFrameOptions.ToString().ShouldBe("DENY");
        headers["Referrer-Policy"].ToString().ShouldBe("no-referrer");
        headers["Permissions-Policy"].ToString().ShouldBe(SecurityHeaders.PermissionsPolicy);
        headers["Cross-Origin-Opener-Policy"].ToString().ShouldBe("same-origin");
        headers["Cross-Origin-Resource-Policy"].ToString().ShouldBe("same-origin");
        headers.ContainsKey("Server").ShouldBeFalse();
        headers.ContainsKey("X-Powered-By").ShouldBeFalse();
    }

    [Fact]
    public void A_signed_in_response_is_not_stored()
    {
        var context = SignedIn();

        SecurityHeaders.Apply(context, "default-src 'none'", "no-referrer");

        context.Response.Headers.CacheControl.ToString().ShouldBe("no-store");
    }

    [Fact]
    public void A_signed_in_response_keeps_the_caching_its_endpoint_chose()
    {
        var context = SignedIn();
        context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";

        SecurityHeaders.Apply(context, "default-src 'none'", "no-referrer");

        context.Response.Headers.CacheControl.ToString().ShouldBe("public, max-age=31536000, immutable");
    }

    [Fact]
    public void An_anonymous_response_keeps_its_caching_to_the_endpoint()
    {
        var context = new DefaultHttpContext();

        SecurityHeaders.Apply(context, "default-src 'none'", "no-referrer");

        context.Response.Headers.ContainsKey("Cache-Control").ShouldBeFalse();
    }

    [Fact]
    public void The_permissions_policy_turns_off_every_sensor_and_payment_feature()
    {
        foreach (var feature in new[] { "camera", "microphone", "geolocation", "payment", "usb", "browsing-topics" })
        {
            SecurityHeaders.PermissionsPolicy.ShouldContain($"{feature}=()");
        }
    }

    private static DefaultHttpContext SignedIn() =>
        new() { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u1")], authenticationType: "Test")) };
}
