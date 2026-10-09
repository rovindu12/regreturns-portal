namespace RegReturns.Web.Models.Demo;

/// <summary>Data for the demo banner shown on every page in demo mode.</summary>
/// <param name="NextReset">When the demo resets next, if it is scheduled.</param>
/// <param name="Now">The current time, to say how far away that is.</param>
public sealed record DemoBannerViewModel(DateTimeOffset? NextReset, DateTimeOffset Now)
{
    /// <summary>Gets how long until the next reset, in words, such as <c>in 5 hours</c>.</summary>
    public string? Countdown
    {
        get
        {
            if (NextReset is not { } next)
            {
                return null;
            }

            var left = next - Now;
            if (left < TimeSpan.FromMinutes(1))
            {
                return "in under a minute";
            }

            if (left < TimeSpan.FromHours(1))
            {
                var minutes = (int)left.TotalMinutes;
                return minutes == 1 ? "in 1 minute" : $"in {minutes} minutes";
            }

            var hours = (int)left.TotalHours;
            return hours == 1 ? "in 1 hour" : $"in {hours} hours";
        }
    }
}
