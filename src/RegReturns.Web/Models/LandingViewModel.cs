using System.Security.Claims;

using RegReturns.Web.Navigation;

namespace RegReturns.Web.Models;

/// <summary>Data for an area's landing page.</summary>
/// <param name="Area">The area.</param>
/// <param name="User">Who is signed in.</param>
public sealed record LandingViewModel(PortalArea Area, SignedInUserViewModel User)
{
    /// <summary>The shared view that renders every area's landing page.</summary>
    public const string ViewName = "Landing";

    /// <summary>Creates the landing page model for the current user.</summary>
    /// <param name="area">The area.</param>
    /// <param name="principal">The signed-in user.</param>
    /// <returns>The view model.</returns>
    public static LandingViewModel Create(PortalArea area, ClaimsPrincipal principal) => new(area, SignedInUserViewModel.From(principal));
}
