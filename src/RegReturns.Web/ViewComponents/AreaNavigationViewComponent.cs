using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Web.Navigation;

namespace RegReturns.Web.ViewComponents;

/// <summary>Navigation links to the areas the current user's policies allow.</summary>
/// <param name="authorizationService">Evaluates each area's policy.</param>
public sealed class AreaNavigationViewComponent(IAuthorizationService authorizationService) : ViewComponent
{
    /// <summary>Renders the links.</summary>
    /// <returns>The view with the allowed areas.</returns>
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var allowed = new List<PortalArea>();
        if (UserClaimsPrincipal.Identity?.IsAuthenticated == true)
        {
            foreach (var area in PortalAreas.All)
            {
                if ((await authorizationService.AuthorizeAsync(UserClaimsPrincipal, area.Policy)).Succeeded)
                {
                    allowed.Add(area);
                }
            }
        }

        return View(allowed);
    }
}
