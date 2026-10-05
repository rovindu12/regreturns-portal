using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

using RegReturns.Web.Identity;

namespace RegReturns.UnitTests.Web;

public sealed class DistributedSessionDenyListTests
{
    private readonly DistributedSessionDenyList _denyList =
        new(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));

    [Fact]
    public async Task Denied_session_is_reported_as_denied()
    {
        await _denyList.DenyAsync("wso2-session-1", TestContext.Current.CancellationToken);

        (await _denyList.IsDeniedAsync("wso2-session-1", TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task Other_sessions_are_not_denied()
    {
        await _denyList.DenyAsync("wso2-session-1", TestContext.Current.CancellationToken);

        (await _denyList.IsDeniedAsync("wso2-session-2", TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public void Denied_sessions_are_remembered_longer_than_the_longest_portal_session()
    {
        DistributedSessionDenyList.Retention.ShouldBeGreaterThan(PortalSession.AbsoluteLifetime);
    }
}
