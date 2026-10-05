using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Application.Authorization;
using RegReturns.Application.Messaging;
using RegReturns.Application.Reporting;
using RegReturns.Domain.Common;
using RegReturns.Web.Models;
using RegReturns.Web.Models.Reports;
using RegReturns.Web.Navigation;

namespace RegReturns.Web.Controllers;

/// <summary>
/// The reports area (plan §6, ADR 0028): the compliance grid, overdue returns, validation findings and key ratios of one
/// return type, and the compliance report as Excel or PDF. Every portal role opens it; bank staff see their own bank.
/// </summary>
[Route(PortalAreas.ReportsRoute)]
[Authorize(Policy = Policies.ReportsView)]
public sealed class ReportsController : Controller
{
    /// <summary>Shows the dashboard of a return type.</summary>
    /// <param name="returnType">The return type's code, or none for the first.</param>
    /// <param name="handler">The dashboard query.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The dashboard, or 404 for an unknown return type.</returns>
    [HttpGet]
    public async Task<IActionResult> IndexAsync(
        [FromQuery] string? returnType,
        [FromServices] IQueryHandler<GetReportsDashboard, Result<ReportsDashboard>> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var dashboard = await handler.HandleAsync(new GetReportsDashboard(returnType), cancellationToken);
        if (dashboard.IsFailure && dashboard.Error!.Is(ReportErrors.UnknownReturnType))
        {
            return NotFound();
        }

        return View(dashboard.IsSuccess ? new ReportsViewModel(dashboard.Value, null) : new ReportsViewModel(null, dashboard.Error!.Message));
    }

    /// <summary>Downloads the compliance report of a return type. The export is recorded in the audit trail.</summary>
    /// <param name="returnType">The return type's code, or none for the first.</param>
    /// <param name="format"><c>xlsx</c> or <c>pdf</c>.</param>
    /// <param name="handler">The export command.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The file, 400 for a missing or unknown format, or 404 for an unknown return type.</returns>
    [HttpGet("compliance/export")]
    public async Task<IActionResult> ExportAsync(
        [FromQuery] string? returnType,
        [FromQuery] ReportFormat? format,
        [FromServices] ICommandHandler<ExportComplianceReport, Result<ReportFile>> handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!ModelState.IsValid || format is not { } chosen || !Enum.IsDefined(chosen))
        {
            return BadRequest();
        }

        var file = await handler.HandleAsync(new ExportComplianceReport(returnType, chosen), cancellationToken);
        if (file.IsFailure)
        {
            if (file.Error!.Is(ReportErrors.UnknownReturnType))
            {
                return NotFound();
            }

            TempData.Error(file.Error.Message);
            return RedirectToAction("Index", new { returnType });
        }

        return File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
    }
}
