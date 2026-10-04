using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Application.Authorization;
using RegReturns.Web.Models;
using RegReturns.Web.Navigation;

namespace RegReturns.Web.Controllers;

/// <summary>Landing page of the bank area (makers and checkers of a licensed bank).</summary>
[Route(PortalAreas.BankRoute)]
[Authorize(Policy = Policies.BankAccess)]
public sealed class BankController : Controller
{
    /// <summary>Shows who is signed in and what the area will offer.</summary>
    /// <returns>The landing page.</returns>
    [HttpGet]
    public IActionResult Index() => View(LandingViewModel.ViewName, LandingViewModel.Create(PortalAreas.Bank, User));
}
