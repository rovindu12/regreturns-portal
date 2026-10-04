using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Application.Authorization;
using RegReturns.Web.Models;
using RegReturns.Web.Navigation;

namespace RegReturns.Web.Controllers;

/// <summary>Landing page of the reports area (every portal role).</summary>
[Route(PortalAreas.ReportsRoute)]
[Authorize(Policy = Policies.ReportsView)]
public sealed class ReportsController : Controller
{
    /// <summary>Shows who is signed in and what the area will offer.</summary>
    /// <returns>The landing page.</returns>
    [HttpGet]
    public IActionResult Index() => View(LandingViewModel.ViewName, LandingViewModel.Create(PortalAreas.Reports, User));
}
