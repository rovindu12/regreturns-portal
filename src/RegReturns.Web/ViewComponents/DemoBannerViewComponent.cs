using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using RegReturns.Application.Demo;
using RegReturns.Web.Models.Demo;

namespace RegReturns.Web.ViewComponents;

/// <summary>
/// The banner on every page of the public demo (ADR 0031): fictional data, and when the next reset undoes visitors'
/// changes. Renders nothing outside demo mode. Reads no data: the next reset comes from the schedule.
/// </summary>
/// <param name="options">The demo settings.</param>
/// <param name="schedule">The reset schedule.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class DemoBannerViewComponent(IOptions<DemoOptions> options, IDemoResetSchedule schedule, TimeProvider timeProvider)
    : ViewComponent
{
    /// <summary>Renders the banner.</summary>
    /// <returns>The banner, or nothing outside demo mode.</returns>
    public IViewComponentResult Invoke()
    {
        if (!options.Value.Enabled)
        {
            return Content(string.Empty);
        }

        var now = timeProvider.GetUtcNow();
        return View(new DemoBannerViewModel(schedule.NextAfter(now), now));
    }
}
