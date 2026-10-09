using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using RegReturns.ServiceDefaults.Web;

namespace RegReturns.UnitTests.ServiceDefaults;

public sealed class ReverseProxyOptionsTests
{
    [Fact]
    public void Without_networks_the_app_is_not_behind_a_proxy()
    {
        new ReverseProxyOptions().BehindProxy.ShouldBeFalse();
    }

    [Fact]
    public void Forwarded_headers_are_believed_only_from_the_configured_networks()
    {
        using var app = Build(("ReverseProxy:KnownNetworks:0", "172.30.0.0/24"), ("ReverseProxy:KnownNetworks:1", "fd00:30::/64"));

        var options = app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.KnownIPNetworks.ShouldBe([System.Net.IPNetwork.Parse("172.30.0.0/24"), System.Net.IPNetwork.Parse("fd00:30::/64")]);
        options.KnownProxies.ShouldBeEmpty();
        options.ForwardedHeaders.ShouldBe(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
    }

    [Theory]
    [InlineData("caddy")]
    [InlineData("300.1.1.0/24")]
    public void A_network_not_in_cidr_notation_fails_validation(string network)
    {
        using var app = Build(("ReverseProxy:KnownNetworks:0", network));

        var error = Should.Throw<OptionsValidationException>(() => app.Services.GetRequiredService<IOptions<ReverseProxyOptions>>().Value);

        error.Message.ShouldContain("ReverseProxy:KnownNetworks");
    }

    private static WebApplication Build(params (string Key, string Value)[] settings)
    {
        var builder = WebApplication.CreateBuilder();
        foreach (var (key, value) in settings)
        {
            builder.Configuration[key] = value;
        }

        builder.AddServiceDefaults("regreturns-unit-test");
        return builder.Build();
    }
}
