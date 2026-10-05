using System.Net;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using RegReturns.Domain.Auditing;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;
using RegReturns.Web.Controllers;
using RegReturns.Web.Identity;

namespace RegReturns.IntegrationTests.Web;

[Collection(HostedAppsDefinition.Name)]
public sealed class BackchannelLogoutEndpointTests : IClassFixture<PortalDatabaseFixture>, IDisposable
{
    // Must match the dummy settings of TestAuth.UseTestAuth.
    private const string Issuer = "https://iam.test.invalid/oauth2/token";
    private const string ClientId = "regreturns-portal-test";

    private readonly PortalDatabaseFixture _database;
    private readonly RSA _signingRsa = RSA.Create(2048);
    private readonly RsaSecurityKey _signingKey;
    private readonly WebApplicationFactory<Program> _factory;

    public BackchannelLogoutEndpointTests(PortalDatabaseFixture database)
    {
        _database = database;
        _signingKey = new RsaSecurityKey(_signingRsa) { KeyId = "test-signing" };
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(_signingKey);

        // Stands in for WSO2's discovery document and JWKS, which the tests cannot reach.
        _factory = PortalHost.Create(
            database.ConnectionString,
            services => services.Configure<OpenIdConnectOptions>(
                OpenIdConnectDefaults.AuthenticationScheme, options => options.Configuration = configuration));
    }

    [Fact]
    public async Task Garbage_logout_token_is_rejected()
    {
        using var client = _factory.CreateClient();

        var response = await PostAsync(client, "not-a-jwt");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Request_without_a_logout_token_is_rejected()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync(PortalPaths.BackchannelLogout, new FormUrlEncodedContent([]), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Valid_logout_token_ends_the_session_and_is_audited()
    {
        using var client = _factory.CreateClient();
        var subject = $"user-{Guid.NewGuid():N}";
        var sessionId = $"sid-{Guid.NewGuid():N}";

        var response = await PostAsync(client, LogoutToken(subject, sessionId));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        (await _factory.Services.GetRequiredService<ISessionDenyList>().IsDeniedAsync(sessionId, TestContext.Current.CancellationToken))
            .ShouldBeTrue();
        await using var context = SqlServerFixture.CreateContext(_database.ConnectionString);
        var entry = await context.AuditEntries.SingleAsync(e => e.ActorSubjectId == subject, TestContext.Current.CancellationToken);
        entry.Action.ShouldBe(AuditAction.SignOut);
        entry.Details.ShouldBe(BackchannelLogoutController.AuditDetails);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _signingRsa.Dispose();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string logoutToken) =>
        client.PostAsync(
            PortalPaths.BackchannelLogout,
            new FormUrlEncodedContent([new KeyValuePair<string, string>(BackchannelLogoutController.LogoutTokenField, logoutToken)]),
            TestContext.Current.CancellationToken);

    private string LogoutToken(string subject, string sessionId) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = ClientId,
        IssuedAt = DateTime.UtcNow,
        Expires = DateTime.UtcNow.AddMinutes(2),
        SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        Claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = subject,
            ["sid"] = sessionId,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            [LogoutTokenValidator.EventsClaim] = new Dictionary<string, object>
            {
                [LogoutTokenValidator.BackchannelLogoutEvent] = new Dictionary<string, object>(),
            },
        },
    });
}
