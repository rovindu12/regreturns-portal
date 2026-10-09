using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// Every form the portal accepts is protected against cross-site request forgery (ADR 0033): a POST without the
/// antiforgery token is refused with 400 before the action runs, even for a user every policy lets through. Only
/// WSO2's back-channel logout, a server-to-server call with a signed logout token, is exempt.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed partial class PortalAntiforgeryTests(SqlServerFixture sql) : IDisposable
{
    private static readonly string[] Exempt = ["BackchannelLogout.Logout"];

    private readonly WebApplicationFactory<Program> _factory = PortalHost.Create(sql.ConnectionString);

    [Fact]
    public async Task Every_post_without_an_antiforgery_token_is_refused()
    {
        var posts = PostEndpoints().Where(post => !Exempt.Contains(post.Name)).ToList();
        posts.Count.ShouldBeGreaterThan(25, "The test must see the portal's forms.");
        using var client = EveryRoleAndTwoStepSignIn();

        var accepted = new List<string>();
        foreach (var (name, path) in posts)
        {
            using var form = new FormUrlEncodedContent([new KeyValuePair<string, string>("comment", "Forged request")]);
            var response = await client.PostAsync(new Uri(path, UriKind.Relative), form, TestContext.Current.CancellationToken);
            if (response.StatusCode != HttpStatusCode.BadRequest)
            {
                accepted.Add($"{name} {path}: {(int)response.StatusCode}");
            }
        }

        accepted.ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_back_channel_logout_skips_the_check()
    {
        PostEndpoints().Select(post => post.Name).ShouldContain(Exempt[0]);
        _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAntiforgeryMetadata>() is { RequiresValidation: false }
                || endpoint.Metadata.GetMetadata<IgnoreAntiforgeryTokenAttribute>() is not null)
            .Select(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action ? $"{action.ControllerName}.{action.ActionName}" : endpoint.DisplayName)
            .ShouldBe(Exempt);
    }

    [Fact]
    public void The_check_comes_from_one_global_filter_not_from_attributes_on_actions()
    {
        // A per-action [ValidateAntiForgeryToken] adds nothing here, and makes CodeQL (cs/web/missing-token-validation),
        // which does not see ASP.NET Core's global filters, report every other form as unprotected.
        _factory.Services.GetRequiredService<IOptions<MvcOptions>>().Value.Filters
            .ShouldContain(filter => filter is AutoValidateAntiforgeryTokenAttribute);
        _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<ValidateAntiForgeryTokenAttribute>() is not null)
            .Select(endpoint => endpoint.DisplayName)
            .ShouldBeEmpty();
    }

    public void Dispose() => _factory.Dispose();

    private IEnumerable<(string Name, string Path)> PostEndpoints() =>
        _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post, StringComparer.Ordinal) == true)
            .Select(endpoint => (
                endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action ? $"{action.ControllerName}.{action.ActionName}" : endpoint.DisplayName ?? "?",
                "/" + RouteParameter().Replace(endpoint.RoutePattern.RawText!.TrimStart('/'), match =>
                    match.Groups["constraint"].Value.Contains("guid", StringComparison.Ordinal) ? Guid.CreateVersion7().ToString() : "X1")));

    // A user every policy admits: all roles, a bank, and the TOTP step. Antiforgery runs before any action, so the
    // user needs no portal record and nothing is written.
    private HttpClient EveryRoleAndTwoStepSignIn()
    {
        var claims = new List<(string Type, string Value)> { (ClaimNames.Subject, "it-forgery"), (ClaimNames.Name, "it-forgery") };
        claims.AddRange(Enum.GetValues<Role>().Select(role => (ClaimNames.Roles, RoleNames.For(role))));
        claims.Add((ClaimNames.InstitutionId, DemoBank.Harbourline));
        claims.Add((ClaimNames.AuthenticationMethods, SupervisionPortal.Totp));
        var client = _factory.CreateClientAs([.. claims]);
        client.BaseAddress = PortalHost.BaseAddress;
        return client;
    }

    [GeneratedRegex("\\{(?<name>[^}:?]+)(?<constraint>:[^}?]*)?\\??\\}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RouteParameter();
}
