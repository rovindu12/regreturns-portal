using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Application.Messaging;
using RegReturns.Application.Portal;
using RegReturns.ServiceDefaults.Web;
using RegReturns.Web.Models;

namespace RegReturns.Web.Controllers;

/// <summary>Public home and error pages.</summary>
[AllowAnonymous]
public sealed class HomeController : Controller
{
    /// <summary>Shows the home page with headline figures. Routed as <c>Index</c> (MVC drops the Async suffix).</summary>
    /// <param name="handler">The summary query handler.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The home page.</returns>
    [HttpGet]
    public async Task<IActionResult> IndexAsync(
        [FromServices] IQueryHandler<GetPortalSummary, PortalSummary> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var summary = await handler.HandleAsync(new GetPortalSummary(), cancellationToken);
        return View(summary);
    }

    /// <summary>Shows a friendly error page with a reference users can quote to support.</summary>
    /// <returns>The error page.</returns>
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel(WebDefaultsExtensions.CurrentTraceId(HttpContext)));
}
