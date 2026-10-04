using System.Security.Cryptography;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using NSubstitute;

using RegReturns.Infrastructure.Identity.Wso2;
using RegReturns.Web.Identity;

namespace RegReturns.UnitTests.Web;

public sealed class LogoutTokenValidatorTests : IDisposable
{
    private const string Issuer = "https://iam.example.test/oauth2/token";
    private const string ClientId = "regreturns-portal";

    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly RSA _wso2Rsa = RSA.Create(2048);
    private readonly RSA _otherRsa = RSA.Create(2048);
    private readonly RsaSecurityKey _wso2Key;
    private readonly RsaSecurityKey _otherKey;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> _configurationManager =
        Substitute.For<IConfigurationManager<OpenIdConnectConfiguration>>();

    private readonly LogoutTokenValidator _validator;

    public LogoutTokenValidatorTests()
    {
        _wso2Key = new RsaSecurityKey(_wso2Rsa) { KeyId = "wso2-signing" };
        _otherKey = new RsaSecurityKey(_otherRsa) { KeyId = "wso2-signing" };

        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(_wso2Key);
        _configurationManager.GetConfigurationAsync(Arg.Any<CancellationToken>()).Returns(configuration);

        var oidc = Substitute.For<IOptionsMonitor<OpenIdConnectOptions>>();
        oidc.Get(OpenIdConnectDefaults.AuthenticationScheme).Returns(new OpenIdConnectOptions { ConfigurationManager = _configurationManager });

        _validator = new LogoutTokenValidator(
            oidc,
            Options.Create(new Wso2Options { Authority = new Uri("https://iam.example.test/") }),
            Options.Create(new OidcOptions { ClientId = ClientId, ClientSecret = "unused" }),
            new FakeTimeProvider(Now),
            NullLogger<LogoutTokenValidator>.Instance);
    }

    [Fact]
    public async Task Valid_logout_token_returns_the_session_and_subject()
    {
        var result = await _validator.ValidateAsync(Token(), TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeTrue(result.FailureReason);
        result.SessionId.ShouldBe("wso2-session-1");
        result.Subject.ShouldBe("user-1");
    }

    [Fact]
    public async Task Logout_token_without_an_expiry_is_accepted_while_recent()
    {
        var result = await _validator.ValidateAsync(Token(d => d.Expires = null), TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeTrue(result.FailureReason);
    }

    [Fact]
    public async Task Logout_token_from_another_issuer_is_rejected()
    {
        var result = await _validator.ValidateAsync(Token(d => d.Issuer = "https://evil.example.test/oauth2/token"), TestContext.Current.CancellationToken);

        result.FailureReason.ShouldBe(LogoutTokenFailures.Issuer);
    }

    [Fact]
    public async Task Logout_token_for_another_audience_is_rejected()
    {
        var result = await _validator.ValidateAsync(Token(d => d.Audience = "another-client"), TestContext.Current.CancellationToken);

        result.FailureReason.ShouldBe(LogoutTokenFailures.Audience);
    }

    [Fact]
    public async Task Logout_token_without_the_logout_event_is_rejected()
    {
        var result = await _validator.ValidateAsync(Token(d => d.Claims.Remove(LogoutTokenValidator.EventsClaim)), TestContext.Current.CancellationToken);

        result.FailureReason.ShouldBe(LogoutTokenFailures.MissingEvent);
    }

    [Fact]
    public async Task Logout_token_with_another_event_is_rejected()
    {
        var result = await _validator.ValidateAsync(
            Token(d => d.Claims[LogoutTokenValidator.EventsClaim] = new Dictionary<string, object> { ["urn:example:other"] = new Dictionary<string, object>() }),
            TestContext.Current.CancellationToken);

        result.FailureReason.ShouldBe(LogoutTokenFailures.MissingEvent);
    }

    [Fact]
    public async Task Logout_token_with_a_nonce_is_rejected()
    {
        var result = await _validator.ValidateAsync(Token(d => d.Claims[JwtRegisteredClaimNames.Nonce] = "n-1"), TestContext.Current.CancellationToken);

        result.FailureReason.ShouldBe(LogoutTokenFailures.NoncePresent);
    }

    [Fact]
    public async Task Expired_logout_token_is_rejected()
    {
        var result = await _validator.ValidateAsync(
            Token(d =>
            {
                d.IssuedAt = Now.AddMinutes(-4).UtcDateTime;
                d.Expires = Now.AddMinutes(-3).UtcDateTime;
            }),
            TestContext.Current.CancellationToken);

        result.FailureReason.ShouldBe(LogoutTokenFailures.Lifetime);
    }

    [Fact]
    public async Task Logout_token_issued_too_long_ago_is_rejected()
    {
        var result = await _validator.ValidateAsync(
            Token(d =>
            {
                d.IssuedAt = Now.AddMinutes(-30).UtcDateTime;
                d.Expires = null;
            }),
            TestContext.Current.CancellationToken);

        result.FailureReason.ShouldBe(LogoutTokenFailures.Lifetime);
    }

    [Fact]
    public async Task Logout_token_with_a_bad_signature_is_rejected()
    {
        var result = await _validator.ValidateAsync(Token(signingKey: _otherKey), TestContext.Current.CancellationToken);

        result.FailureReason.ShouldBe(LogoutTokenFailures.Signature);
    }

    [Fact]
    public async Task Logout_token_without_a_session_id_is_rejected()
    {
        var result = await _validator.ValidateAsync(Token(d => d.Claims.Remove("sid")), TestContext.Current.CancellationToken);

        result.FailureReason.ShouldBe(LogoutTokenFailures.MissingSessionId);
    }

    [Fact]
    public async Task Garbage_is_rejected_without_fetching_signing_keys()
    {
        var result = await _validator.ValidateAsync("not-a-jwt", TestContext.Current.CancellationToken);

        result.FailureReason.ShouldBe(LogoutTokenFailures.Malformed);
        await _configurationManager.DidNotReceive().GetConfigurationAsync(Arg.Any<CancellationToken>());
    }

    public void Dispose()
    {
        _wso2Rsa.Dispose();
        _otherRsa.Dispose();
    }

    private string Token(Action<SecurityTokenDescriptor>? change = null, SecurityKey? signingKey = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ClientId,
            IssuedAt = Now.AddSeconds(-5).UtcDateTime,
            Expires = Now.AddMinutes(2).UtcDateTime,
            SigningCredentials = new SigningCredentials(signingKey ?? _wso2Key, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = "user-1",
                ["sid"] = "wso2-session-1",
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                [LogoutTokenValidator.EventsClaim] = new Dictionary<string, object>
                {
                    [LogoutTokenValidator.BackchannelLogoutEvent] = new Dictionary<string, object>(),
                },
            },
        };
        change?.Invoke(descriptor);
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }
}
