using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using RegReturns.Application.Demo;
using RegReturns.Application.Messaging;
using RegReturns.Infrastructure.Identity.Wso2;
using RegReturns.Web.Models.Demo;

namespace RegReturns.Web.Controllers;

/// <summary>
/// The public demo pages (ADR 0031): the demo accounts with their published password and TOTP secrets, the Swagger
/// demo client, and the guided scenario. Not found outside demo mode.
/// </summary>
/// <param name="options">The demo settings.</param>
[AllowAnonymous]
[Route(Route)]
public sealed class DemoController(IOptions<DemoOptions> options) : Controller
{
    /// <summary>The route prefix of the demo pages.</summary>
    public const string Route = "demo";

    /// <summary>Shows the demo accounts and the API client. Never cached: it carries the published credentials.</summary>
    /// <param name="accounts">The demo accounts query.</param>
    /// <param name="status">The reset history query.</param>
    /// <param name="wso2">The WSO2 settings, for the token endpoint.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The demo page, or 404 outside demo mode.</returns>
    [HttpGet("")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> IndexAsync(
        [FromServices] IQueryHandler<GetDemoAccounts, IReadOnlyList<DemoAccount>> accounts,
        [FromServices] IQueryHandler<GetDemoStatus, DemoStatus> status,
        [FromServices] IOptions<Wso2Options> wso2,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(wso2);
        if (!options.Value.Enabled)
        {
            return NotFound();
        }

        var list = await accounts.HandleAsync(new GetDemoAccounts(), cancellationToken);
        var resets = await status.HandleAsync(new GetDemoStatus(1), cancellationToken);
        var tokenEndpoint = wso2.Value.Authority is null ? null : wso2.Value.TokenEndpoint;
        return View(DemoPageViewModel.Create(list, options.Value, tokenEndpoint, resets));
    }

    /// <summary>Shows the guided scenario: one return from draft to approval, then the audit trail and reports.</summary>
    /// <returns>The guide, or 404 outside demo mode.</returns>
    [HttpGet("guide")]
    public IActionResult Guide() => options.Value.Enabled ? View() : NotFound();
}
