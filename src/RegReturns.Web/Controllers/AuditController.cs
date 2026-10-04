using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Application.Authorization;
using RegReturns.Web.Models;
using RegReturns.Web.Navigation;

namespace RegReturns.Web.Controllers;

/// <summary>Landing page of the audit area (auditors and administrators).</summary>
[Route(PortalAreas.AuditRoute)]
[Authorize(Policy = Policies.AuditRead)]
public sealed class AuditController : Controller
{
    /// <summary>Shows who is signed in and what the area will offer.</summary>
    /// <returns>The landing page.</returns>
    [HttpGet]
    public IActionResult Index() => View(LandingViewModel.ViewName, LandingViewModel.Create(PortalAreas.Audit, User));
}
