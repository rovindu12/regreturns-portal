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
    /// <summary>Shows the administration tools, such as return templates, and who is signed in.</summary>
    /// <returns>The landing page.</returns>
    [HttpGet]
    public IActionResult Index() => View(LandingViewModel.Create(PortalAreas.Admin, User));
}
