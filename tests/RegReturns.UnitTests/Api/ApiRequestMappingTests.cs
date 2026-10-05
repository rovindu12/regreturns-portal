using System.Net;
using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Http;

using RegReturns.Api.Contracts;
using RegReturns.Api.RateLimiting;
using RegReturns.Application.Identity;

namespace RegReturns.UnitTests.Api;

public sealed class ApiRequestMappingTests
{
    [Fact]
    public void Delivered_values_keep_their_raw_text()
    {
        var values = Parse("""{"A":"1,234.50","B":1.50,"C":12E3,"D":true,"E":false,"F":null}""");

        var raw = values.ToRawValues(out var invalid);

        invalid.ShouldBeEmpty();
        raw["A"].ShouldBe("1,234.50");
        raw["B"].ShouldBe("1.50");
        raw["C"].ShouldBe("12E3");
        raw["D"].ShouldBe("true");
        raw["E"].ShouldBe("false");
        raw["F"].ShouldBeNull();
    }

    [Fact]
    public void Objects_and_arrays_are_not_values()
    {
        var values = Parse("""{"A":"1","B":{"value":1},"C":[1]}""");

        var raw = values.ToRawValues(out var invalid);

        invalid.ShouldBe(["B", "C"], ignoreOrder: true);
        raw.Keys.ShouldBe(["A"]);
    }

    [Fact]
    public void A_client_is_limited_by_its_client_id()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimNames.AuthorizedParty, "regreturns-bank-hlb")], "test")),
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

        ApiRateLimiting.PartitionKey(context).ShouldBe("client:regreturns-bank-hlb");
    }

    [Fact]
    public void An_anonymous_caller_is_limited_by_its_address()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");

        ApiRateLimiting.PartitionKey(context).ShouldBe("ip:10.0.0.1");
        ApiRateLimiting.PartitionKey(new DefaultHttpContext()).ShouldBe("ip:unknown");
    }

    private static Dictionary<string, JsonElement> Parse(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}
