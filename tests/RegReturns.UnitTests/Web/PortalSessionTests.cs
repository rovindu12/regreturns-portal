using Microsoft.AspNetCore.Authentication;

using RegReturns.Web.Identity;

namespace RegReturns.UnitTests.Web;

public sealed class PortalSessionTests
{
    private static readonly DateTimeOffset SignedInAt = new(2026, 10, 4, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Session_before_its_absolute_expiry_has_not_expired()
    {
        var properties = PropertiesExpiringAt(SignedInAt + PortalSession.AbsoluteLifetime);

        PortalSession.HasExpired(properties, SignedInAt.AddHours(7).AddMinutes(59)).ShouldBeFalse();
    }

    [Fact]
    public void Session_at_its_absolute_expiry_has_expired()
    {
        var properties = PropertiesExpiringAt(SignedInAt + PortalSession.AbsoluteLifetime);

        PortalSession.HasExpired(properties, SignedInAt.AddHours(8)).ShouldBeTrue();
    }

    [Fact]
    public void Sliding_renewal_does_not_extend_the_absolute_expiry()
    {
        var properties = PropertiesExpiringAt(SignedInAt + PortalSession.AbsoluteLifetime);
        properties.IssuedUtc = SignedInAt.AddHours(8).AddMinutes(-5);
        properties.ExpiresUtc = properties.IssuedUtc + PortalSession.IdleTimeout;

        PortalSession.HasExpired(properties, SignedInAt.AddHours(8).AddMinutes(1)).ShouldBeTrue();
    }

    [Fact]
    public void Session_without_an_absolute_expiry_counts_as_expired()
    {
        PortalSession.HasExpired(new AuthenticationProperties(), SignedInAt).ShouldBeTrue();
    }

    [Fact]
    public void Absolute_expiry_round_trips_through_the_properties()
    {
        var expiresAt = new DateTimeOffset(2026, 10, 4, 18, 30, 0, TimeSpan.FromHours(2));
        var properties = PropertiesExpiringAt(expiresAt);

        PortalSession.GetAbsoluteExpiry(properties).ShouldBe(expiresAt);
    }

    private static AuthenticationProperties PropertiesExpiringAt(DateTimeOffset expiresAt)
    {
        var properties = new AuthenticationProperties { IssuedUtc = SignedInAt };
        PortalSession.SetAbsoluteExpiry(properties, expiresAt);
        return properties;
    }
}
