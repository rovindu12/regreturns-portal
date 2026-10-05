using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
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
    }

    [Theory]
    [InlineData("Bank", "Submit", Policies.BankSubmitReturn)]
    [InlineData("Supervision", "StartReview", Policies.SupervisionReview)]
    [InlineData("Supervision", "ReturnForCorrection", Policies.SupervisionAccess)]
    [InlineData("Supervision", "Approve", Policies.SupervisionApprove)]
    [InlineData("Supervision", "Reject", Policies.SupervisionApprove)]
    public void Workflow_steps_require_the_policy_of_their_role(string controller, string action, string policy)
    {
        var endpoint = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Single(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is { } descriptor
                && descriptor.ControllerName == controller && descriptor.ActionName == action);

        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy).ShouldContain(policy);
    }

    public void Dispose() => _factory.Dispose();

    // The conventional route itself is registered for URL generation only and never matches a request.
    private static bool IsLinkGenerationOnly(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<ISuppressMatchingMetadata>()?.SuppressMatching == true;

    // MapStaticAssets adds a catch-all for files created after the build when it runs from build output (not publish).
    private static bool IsStaticAssetDevelopmentFallback(Endpoint endpoint) =>
        endpoint is RouteEndpoint { RoutePattern.RawText: "{**path:file}" };
}
