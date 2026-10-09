using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using RegReturns.Application.Demo;
using RegReturns.Application.Messaging;
using RegReturns.Application.Portal;
using RegReturns.ServiceDefaults.Web;
using RegReturns.Web.Models;

namespace RegReturns.Web.Controllers;

/// <summary>Public landing and error pages.</summary>
[AllowAnonymous]
public sealed class HomeController : Controller
{
    /// <summary>
    /// Shows the landing page: what the portal does, how it is built, who does what, and headline figures. Routed as
    /// <c>Index</c> (MVC drops the Async suffix).
    /// </summary>
    /// <param name="handler">The summary query handler.</param>
    /// <param name="demo">The demo settings.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The landing page.</returns>
    [HttpGet]
    public async Task<IActionResult> IndexAsync(
        [FromServices] IQueryHandler<GetPortalSummary, PortalSummary> handler,
        [FromServices] IOptions<DemoOptions> demo,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(demo);
        var summary = await handler.HandleAsync(new GetPortalSummary(), cancellationToken);
        var swagger = demo.Value.ApiBaseUrl is { } api ? new Uri(api, "swagger") : null;
        return View(new HomeViewModel(summary, demo.Value.Enabled, swagger));
    }

    /// <summary>Shows a friendly error page with a reference users can quote to support.</summary>
    /// <returns>The error page.</returns>
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel(WebDefaultsExtensions.CurrentTraceId(HttpContext)));
}
