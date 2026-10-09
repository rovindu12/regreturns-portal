using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Application.Demo;
using RegReturns.Application.Messaging;
using RegReturns.ServiceDefaults;
using RegReturns.Web.Models;
using RegReturns.Web.Status;

namespace RegReturns.Web.Controllers;

/// <summary>The public status page (ADR 0031): the portal, the database and WSO2 in green, amber or red, the version and the demo's resets.</summary>
[AllowAnonymous]
[Route(Route)]
public sealed class StatusController : Controller
{
    /// <summary>The page's route.</summary>
    public const string Route = "status";

    /// <summary>Shows the status. The checks run at most every 15 seconds, whoever asks.</summary>
    /// <param name="status">The readiness report.</param>
    /// <param name="demo">The reset history query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The status page.</returns>
    [HttpGet("")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> IndexAsync(
        [FromServices] PortalStatus status,
        [FromServices] IQueryHandler<GetDemoStatus, DemoStatus> demo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(demo);
        var report = await status.GetAsync(cancellationToken);
        var resets = await demo.HandleAsync(new GetDemoStatus(1), cancellationToken);
        return View(new StatusViewModel(report, BuildInfo.Version, resets));
    }
}
