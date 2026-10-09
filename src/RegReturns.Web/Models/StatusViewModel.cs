using RegReturns.Application.Demo;
using RegReturns.Web.Status;

namespace RegReturns.Web.Models;

/// <summary>Data for the public status page.</summary>
/// <param name="Report">The latest readiness report.</param>
/// <param name="Version">The running build.</param>
/// <param name="Demo">Reset history and schedule (not enabled outside demo mode).</param>
public sealed record StatusViewModel(PortalStatusReport Report, string Version, DemoStatus Demo)
{
    /// <summary>Returns the words for a level.</summary>
    /// <param name="level">The level.</param>
    /// <returns>For example <c>Operational</c>.</returns>
    public static string Label(StatusLevel level) => level switch
    {
        StatusLevel.Operational => "Operational",
        StatusLevel.Degraded => "Degraded",
        _ => "Down",
    };

    /// <summary>Returns the Bootstrap badge class for a level (green, amber or red); the label always says it too.</summary>
    /// <param name="level">The level.</param>
    /// <returns>The CSS class.</returns>
    public static string Badge(StatusLevel level) => level switch
    {
        StatusLevel.Operational => "text-bg-success",
        StatusLevel.Degraded => "text-bg-warning",
        _ => "text-bg-danger",
    };

    /// <summary>Returns the Bootstrap alert class for the overall level.</summary>
    /// <param name="level">The level.</param>
    /// <returns>The CSS class.</returns>
    public static string Alert(StatusLevel level) => level switch
    {
        StatusLevel.Operational => "alert-success",
        StatusLevel.Degraded => "alert-warning",
        _ => "alert-danger",
    };

    /// <summary>Gets the sentence that sums the service up.</summary>
    public string Headline => Report.Overall switch
    {
        StatusLevel.Operational => "All systems operational",
        StatusLevel.Degraded => "Some systems are degraded",
        _ => "Some systems are down",
    };
}
