using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Application.Auditing;
using RegReturns.Application.Authorization;
using RegReturns.Application.Messaging;
using RegReturns.Web.Models;
using RegReturns.Web.Models.Audit;
using RegReturns.Web.Navigation;

namespace RegReturns.Web.Controllers;

/// <summary>
/// The audit area (auditors and administrators): the audit trail, newest first, with filters, and verification of its
/// hash chain (ADR 0016, ADR 0024). Every verification is itself recorded in the trail.
/// </summary>
[Route(PortalAreas.AuditRoute)]
[Authorize(Policy = Policies.AuditRead)]
public sealed class AuditController : Controller
{
    /// <summary>Shows one page of the audit trail.</summary>
    /// <param name="filter">The page and filters from the query string.</param>
    /// <param name="handler">The trail query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The audit trail page.</returns>
    [HttpGet]
    public async Task<IActionResult> IndexAsync(
        [FromQuery] AuditTrailFilterInput filter,
        [FromServices] IQueryHandler<GetAuditTrail, AuditTrailPage> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(handler);
        var trail = await handler.HandleAsync(filter.ToQuery(), cancellationToken);
        return View(new AuditTrailViewModel(trail, filter));
    }

    /// <summary>Verifies the hash chain and shows the outcome on the trail page.</summary>
    /// <param name="handler">The verify command, which also records the verification.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A redirect to the trail page.</returns>
    [HttpPost("verify")]
    public async Task<IActionResult> VerifyAsync(
        [FromServices] ICommandHandler<VerifyAuditChain, ChainVerification> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var result = await handler.HandleAsync(new VerifyAuditChain(), cancellationToken);
        if (result.IsIntact)
        {
            TempData.Success(result.Describe());
        }
        else
        {
            TempData.Error(result.Describe());
        }

        return RedirectToAction("Index");
    }
}
