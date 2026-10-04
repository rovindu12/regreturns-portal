using System.Reflection;
using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using RegReturns.Application.Authorization;
using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;
using RegReturns.Infrastructure.Identity.Authorization;

namespace RegReturns.UnitTests.Authorization;

/// <summary>
/// Evaluates the real RegReturns policies without a host: logging plus <c>AddRegReturnsAuthorization</c>
/// over in-memory configuration, then <see cref="IAuthorizationService"/>.
/// </summary>
internal static class PolicyHarness
{
    public const string Institution = "ALPHA";
    public const string ClientId = "alpha-core-banking";

    /// <summary>Gets every policy name declared in <see cref="Policies"/>.</summary>
    public static IReadOnlyList<string> AllPolicies { get; } =
    [
        .. typeof(Policies).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!),
    ];

    public static async Task<bool> AllowsAsync(string policy, ClaimsPrincipal user, IDictionary<string, string?>? settings = null)
    {
        await using var provider = Build(settings);
        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, resource: null, policy);
        return result.Succeeded;
    }

    public static async Task<bool> FallbackAllowsAsync(ClaimsPrincipal user)
    {
        await using var provider = Build(settings: null);
        var fallback = await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetFallbackPolicyAsync();
        fallback.ShouldNotBeNull();
        var result = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, resource: null, fallback);
        return result.Succeeded;
    }

    public static ServiceProvider Build(IDictionary<string, string?>? settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build();
        return new ServiceCollection().AddLogging().AddRegReturnsAuthorization(configuration).BuildServiceProvider();
    }

    public static Dictionary<string, string?> Mfa(bool enforce) => new() { ["Iam:EnforceMfa"] = enforce ? "true" : "false" };

    /// <summary>A signed-in person holding <paramref name="role"/>: bank roles carry an institution.</summary>
    public static ClaimsPrincipal Person(Role role, bool withTotp = true)
    {
        var claims = new List<(string, string)> { (ClaimNames.Subject, $"sub-{role}"), (ClaimNames.Roles, RoleNames.For(role)) };
        if (role.IsBankRole())
        {
            claims.Add((ClaimNames.InstitutionId, Institution));
        }

        claims.Add((ClaimNames.AuthenticationMethods, "pwd"));
        if (withTotp)
        {
            claims.Add((ClaimNames.AuthenticationMethods, "totp"));
        }

        return Authenticated([.. claims]);
    }

    public static ClaimsPrincipal Authenticated(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "Test", ClaimNames.Name, ClaimNames.Roles));

    public static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());
}
