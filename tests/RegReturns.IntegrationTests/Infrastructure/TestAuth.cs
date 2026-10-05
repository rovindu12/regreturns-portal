using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RegReturns.IntegrationTests.Infrastructure;

/// <summary>
/// Replaces WSO2 sign-in in hosted tests (plan §4.7). A request carries its claims in the <see cref="Header"/>
/// header as a form-encoded list, for example <c>sub=u1&amp;roles=bank_maker&amp;institution_id=ALPHA</c>;
/// repeat a name to add several values. A request without the header is anonymous.
/// </summary>
public static class TestAuth
{
    /// <summary>The authentication scheme name.</summary>
    public const string Scheme = "Test";

    /// <summary>The request header that carries the claims.</summary>
    public const string Header = "X-Test-Claims";

    /// <summary>A 32-byte audit key used only by tests.</summary>
    public const string AuditKey = "dGVzdC1vbmx5LWF1ZGl0LWtleS1ub3QtYS1zZWNyZXQhIQ==";

    /// <summary>
    /// Configures a host for tests: dummy WSO2 settings and audit key, the <see cref="Scheme"/> scheme as the default
    /// for authenticate, challenge and forbid (so nothing tries to reach WSO2), and no WSO2 readiness check.
    /// </summary>
    /// <param name="builder">The web host builder.</param>
    /// <returns>The same builder.</returns>
    public static IWebHostBuilder UseTestAuth(this IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .UseSetting("Wso2:Authority", "https://iam.test.invalid/")
            .UseSetting("Audit:HmacKey", AuditKey)
            .UseSetting("Oidc:ClientId", "regreturns-portal-test")
            .UseSetting("Oidc:ClientSecret", "test-only-client-secret")
            .ConfigureTestServices(services =>
            {
                services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(Scheme, null);
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    options.DefaultScheme = Scheme;
                    options.DefaultAuthenticateScheme = Scheme;
                    options.DefaultChallengeScheme = Scheme;
                    options.DefaultForbidScheme = Scheme;
                    options.DefaultSignInScheme = Scheme;
                    options.DefaultSignOutScheme = Scheme;
                });
                services.PostConfigure<HealthCheckServiceOptions>(options =>
                {
                    foreach (var check in options.Registrations.Where(r => r.Name == "wso2").ToList())
                    {
                        options.Registrations.Remove(check);
                    }
                });
            });
    }

    /// <summary>Creates a client whose requests carry the given claims.</summary>
    /// <typeparam name="TEntryPoint">The host's entry point.</typeparam>
    /// <param name="factory">The factory.</param>
    /// <param name="claims">The claims, as (type, value) pairs.</param>
    /// <returns>The client.</returns>
    public static HttpClient CreateClientAs<TEntryPoint>(this WebApplicationFactory<TEntryPoint> factory, params (string Type, string Value)[] claims)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(Header, Encode(claims));
        return client;
    }

    /// <summary>Encodes claims for the <see cref="Header"/> header.</summary>
    /// <param name="claims">The claims, as (type, value) pairs.</param>
    /// <returns>The header value.</returns>
    public static string Encode(params (string Type, string Value)[] claims) =>
        string.Join('&', claims.Select(c => $"{UrlEncoder.Default.Encode(c.Type)}={UrlEncoder.Default.Encode(c.Value)}"));

    /// <summary>Authenticates requests from the <see cref="Header"/> header.</summary>
    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(Header, out var raw) || string.IsNullOrEmpty(raw))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = QueryHelpers.ParseQuery(raw.ToString())
                .SelectMany(pair => pair.Value.Select(value => new Claim(pair.Key, value ?? string.Empty)));
            var identity = new ClaimsIdentity(claims, TestAuth.Scheme, "name", "roles");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestAuth.Scheme)));
        }
    }
}
