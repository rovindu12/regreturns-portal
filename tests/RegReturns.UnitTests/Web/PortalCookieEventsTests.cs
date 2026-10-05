using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using RegReturns.Application.Identity;
using RegReturns.Web.Identity;

namespace RegReturns.UnitTests.Web;

public sealed class PortalCookieEventsTests
{
    private static readonly DateTimeOffset SignedInAt = new(2026, 10, 4, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(SignedInAt);
    private readonly ISessionDenyList _denyList = Substitute.For<ISessionDenyList>();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private readonly PortalCookieEvents _events;

    public PortalCookieEventsTests() =>
        _events = new PortalCookieEvents(_denyList, _clock, NullLogger<PortalCookieEvents>.Instance);

    [Fact]
    public async Task Signing_in_stamps_an_absolute_expiry_eight_hours_ahead()
    {
        var properties = new AuthenticationProperties();
        var context = new CookieSigningInContext(
            HttpContext(), Scheme(), new CookieAuthenticationOptions(), Principal(), properties, new CookieOptions());

        await _events.SigningIn(context);

        PortalSession.GetAbsoluteExpiry(properties).ShouldBe(SignedInAt.AddHours(8));
    }

    [Fact]
    public async Task Session_within_its_limits_is_kept()
    {
        var context = ValidateContext(SignedInAt.AddHours(8));
        _clock.Advance(TimeSpan.FromHours(7));

        await _events.ValidatePrincipal(context);

        context.Principal.ShouldNotBeNull();
    }

    [Fact]
    public async Task Session_past_its_absolute_expiry_is_rejected_and_signed_out()
    {
        var context = ValidateContext(SignedInAt.AddHours(8));
        _clock.Advance(TimeSpan.FromHours(8).Add(TimeSpan.FromSeconds(1)));

        await _events.ValidatePrincipal(context);

        context.Principal.ShouldBeNull();
        await _authentication.Received(1).SignOutAsync(
            context.HttpContext, CookieAuthenticationDefaults.AuthenticationScheme, Arg.Any<AuthenticationProperties?>());
    }

    [Fact]
    public async Task Session_ended_by_back_channel_logout_is_rejected()
    {
        _denyList.IsDeniedAsync("wso2-session-1", Arg.Any<CancellationToken>()).Returns(true);
        var context = ValidateContext(SignedInAt.AddHours(8));

        await _events.ValidatePrincipal(context);

        context.Principal.ShouldBeNull();
    }

    [Fact]
    public async Task Signing_in_gives_each_ticket_its_own_random_id()
    {
        var first = new AuthenticationProperties();
        var second = new AuthenticationProperties();

        await _events.SigningIn(new CookieSigningInContext(HttpContext(), Scheme(), new CookieAuthenticationOptions(), Principal(), first, new CookieOptions()));
        await _events.SigningIn(new CookieSigningInContext(HttpContext(), Scheme(), new CookieAuthenticationOptions(), Principal(), second, new CookieOptions()));

        PortalSession.TicketDenyKey(first).ShouldNotBeNull();
        PortalSession.TicketDenyKey(first).ShouldNotBe(PortalSession.TicketDenyKey(second));
    }

    [Fact]
    public async Task Ticket_revoked_at_sign_out_is_rejected()
    {
        var context = ValidateContext(SignedInAt.AddHours(8));
        _denyList.IsDeniedAsync(PortalSession.TicketDenyKey(context.Properties)!, Arg.Any<CancellationToken>()).Returns(true);

        await _events.ValidatePrincipal(context);

        context.Principal.ShouldBeNull();
    }

    [Fact]
    public async Task Ticket_without_an_id_is_rejected()
    {
        var context = ValidateContext(SignedInAt.AddHours(8), withTicketId: false);

        await _events.ValidatePrincipal(context);

        context.Principal.ShouldBeNull();
    }

    private CookieValidatePrincipalContext ValidateContext(DateTimeOffset absoluteExpiry, bool withTicketId = true)
    {
        var properties = new AuthenticationProperties { IssuedUtc = SignedInAt, ExpiresUtc = SignedInAt + PortalSession.IdleTimeout };
        PortalSession.SetAbsoluteExpiry(properties, absoluteExpiry);
        if (withTicketId)
        {
            PortalSession.SetNewTicketId(properties);
        }

        var ticket = new AuthenticationTicket(Principal(), properties, CookieAuthenticationDefaults.AuthenticationScheme);
        return new CookieValidatePrincipalContext(HttpContext(), Scheme(), new CookieAuthenticationOptions(), ticket);
    }

    private DefaultHttpContext HttpContext() =>
        new() { RequestServices = new ServiceCollection().AddSingleton(_authentication).BuildServiceProvider() };

    private static AuthenticationScheme Scheme() =>
        new(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity(
        [new Claim(ClaimNames.Subject, "user-1"), new Claim(ClaimNames.SessionId, "wso2-session-1")], PortalClaims.AuthenticationType));
}
