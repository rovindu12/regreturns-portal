using RegReturns.Application.Demo;
using RegReturns.Web.Navigation;

namespace RegReturns.Web.Models.Admin;

/// <summary>Data for the administration landing page.</summary>
/// <param name="Area">The area.</param>
/// <param name="User">Who is signed in.</param>
/// <param name="Demo">Reset history and schedule, shown in demo mode.</param>
public sealed record AdminIndexViewModel(PortalArea Area, SignedInUserViewModel User, DemoStatus Demo);
