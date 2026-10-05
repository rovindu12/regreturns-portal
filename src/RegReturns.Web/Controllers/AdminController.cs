using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Application.Authorization;
using RegReturns.Web.Models;
using RegReturns.Web.Navigation;

namespace RegReturns.Web.Controllers;

/// <summary>Landing page of the administration area (administrators with two-step verification).</summary>
[Route(PortalAreas.AdminRoute)]
[Authorize(Policy = Policies.AdminManage)]
public sealed class AdminController : Controller
{
    /// <summary>Shows who is signed in and what the area will offer.</summary>
    /// <returns>The landing page.</returns>
    [HttpGet]
    public IActionResult Index() => View(LandingViewModel.ViewName, LandingViewModel.Create(PortalAreas.Admin, User));
}
