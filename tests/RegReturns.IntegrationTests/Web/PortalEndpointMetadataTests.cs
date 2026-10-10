using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticAssets;
using Microsoft.Extensions.DependencyInjection;

using RegReturns.Application.Authorization;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Web;

[Collection(HostedAppsDefinition.Name)]
public sealed class PortalEndpointMetadataTests(SqlServerFixture sql) : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = PortalHost.Create(sql.ConnectionString);

    [Fact]
    public void Every_endpoint_names_a_policy_or_allows_anonymous_access()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        var offenders = endpoints
            .Where(endpoint => !IsLinkGenerationOnly(endpoint) && !IsStaticAssetDevelopmentFallback(endpoint))
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null &&
                !endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => !string.IsNullOrWhiteSpace(data.Policy)))
            .Select(endpoint => endpoint.DisplayName)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Every_controller_action_is_checked_by_the_metadata_test()
    {
        var actions = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>())
            .OfType<ControllerActionDescriptor>()
            .Select(action => $"{action.ControllerName}.{action.ActionName}")
            .ToList();

        actions.ShouldContain("Account.SignOut");
        actions.ShouldContain("BackchannelLogout.Logout");
        actions.ShouldContain("Bank.Index");
        actions.ShouldContain("Admin.Index");
        actions.ShouldContain("Templates.Index");
        actions.ShouldContain("Templates.Publish");
        actions.ShouldContain("Admin.ResetDemo");
        actions.ShouldContain("Admin.DisableUser");
        actions.ShouldContain("Demo.Index");
        actions.ShouldContain("Status.Index");
    }

    [Theory]
    [InlineData("Bank", "Submit", Policies.BankSubmitReturn)]
    [InlineData("Supervision", "StartReview", Policies.SupervisionReview)]
    [InlineData("Supervision", "ReturnForCorrection", Policies.SupervisionAccess)]
    [InlineData("Supervision", "Approve", Policies.SupervisionApprove)]
    [InlineData("Supervision", "Reject", Policies.SupervisionApprove)]
    [InlineData("Supervision", "GenerateInsight", Policies.SupervisionAccess)]
    public void Workflow_steps_require_the_policy_of_their_role(string controller, string action, string policy)
    {
        EndpointOf(controller, action).Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).ShouldContain(policy);
    }

    [Theory]
    [InlineData("Admin", "ResetDemo")]
    [InlineData("Admin", "Users")]
    [InlineData("Admin", "DisableUser")]
    [InlineData("Admin", "EnableUser")]
    [InlineData("Admin", "OpenTotpEnrolment")]
    [InlineData("Admin", "Diagnostics")]
    public void Demo_reset_and_user_access_need_an_administrator_with_two_step_sign_in(string controller, string action)
    {
        var endpoint = EndpointOf(controller, action);

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).ShouldContain(Policies.AdminManage);
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
    }

    [Theory]
    [InlineData("Home", "Index")]
    [InlineData("Demo", "Index")]
    [InlineData("Demo", "Guide")]
    [InlineData("Status", "Index")]
    public void Public_pages_allow_anonymous_visitors(string controller, string action)
    {
        EndpointOf(controller, action).Metadata.GetMetadata<IAllowAnonymous>().ShouldNotBeNull();
    }

    [Fact]
    public void Only_public_pages_sign_in_pages_the_back_channel_logout_and_health_allow_anonymous_access()
    {
        var anonymous = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => !IsLinkGenerationOnly(endpoint) && !IsStaticAssetDevelopmentFallback(endpoint) && !IsStaticFile(endpoint))
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action
                ? $"{action.ControllerName}.{action.ActionName}"
                : endpoint.RoutePattern.RawText)
            .Order(StringComparer.Ordinal)
            .ToList();

        anonymous.ShouldBe(
        [
            "/health/live",
            "/health/ready",
            "Account.AccessDenied",
            "Account.SignIn",
            "Account.SignInFailed",
            "Account.SignedOut",
            "BackchannelLogout.Logout",
            "Demo.Guide",
            "Demo.Index",
            "Home.Error",
            "Home.HttpError",
            "Home.Index",
            "Status.Index",
        ]);
    }

    public void Dispose() => _factory.Dispose();

    // Files under wwwroot, mapped by MapStaticAssets: one endpoint per file, all public by design.
    private static bool IsStaticFile(RouteEndpoint endpoint) => endpoint.Metadata.GetMetadata<StaticAssetDescriptor>() is not null;

    private Endpoint EndpointOf(string controller, string action) =>
        _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Single(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is { } descriptor
                && descriptor.ControllerName == controller && descriptor.ActionName == action);

    // The conventional route itself is registered for URL generation only and never matches a request.
    private static bool IsLinkGenerationOnly(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<ISuppressMatchingMetadata>()?.SuppressMatching == true;

    // MapStaticAssets adds a catch-all for files created after the build when it runs from build output (not publish).
    private static bool IsStaticAssetDevelopmentFallback(Endpoint endpoint) =>
        endpoint is RouteEndpoint { RoutePattern.RawText: "{**path:file}" };
}
