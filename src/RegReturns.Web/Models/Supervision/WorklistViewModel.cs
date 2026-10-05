using RegReturns.Application.Supervision;

namespace RegReturns.Web.Models.Supervision;

/// <summary>The supervision worklist page.</summary>
/// <param name="Worklist">The worklist, or <see langword="null"/> when it could not be loaded.</param>
/// <param name="Problem">Why it could not be loaded.</param>
/// <param name="Filter">The filter in effect.</param>
public sealed record WorklistViewModel(SupervisionWorklist? Worklist, string? Problem, WorklistFilterInput Filter)
{
    /// <summary>Gets the worklist's sections in page order.</summary>
    public IReadOnlyList<WorklistSection> Sections => Worklist is null
        ? []
        :
        [
            new("to-pick-up", "Waiting for a reviewer", "No submitted returns are waiting.", WorklistSectionKind.ToPickUp, Worklist.ToPickUp),
            new("under-review", "Under review", "No returns are under review.", WorklistSectionKind.UnderReview, Worklist.UnderReview),
            new("with-banks", "Sent back to banks", "No returns are with banks for correction.", WorklistSectionKind.WithBanks, Worklist.WithBanks),
            new(
                "decided",
                $"Decided in the last {SupervisionWorklistHandler.DecidedWithinDays} days",
                "No returns were decided recently.",
                WorklistSectionKind.Decided,
                Worklist.RecentlyDecided),
        ];
}

/// <summary>Which worklist section a table shows; it decides the last columns.</summary>
public enum WorklistSectionKind
{
    /// <summary>Submitted returns waiting for a reviewer.</summary>
    ToPickUp = 1,

    /// <summary>Returns under review.</summary>
    UnderReview = 2,

    /// <summary>Returns sent back for correction.</summary>
    WithBanks = 3,

    /// <summary>Approved and rejected returns.</summary>
    Decided = 4,
}

/// <summary>One section of the worklist.</summary>
/// <param name="Id">The section's HTML id.</param>
/// <param name="Title">The heading.</param>
/// <param name="EmptyText">What to say when it is empty.</param>
/// <param name="Kind">Which columns it shows.</param>
/// <param name="Rows">The returns.</param>
public sealed record WorklistSection(string Id, string Title, string EmptyText, WorklistSectionKind Kind, IReadOnlyList<WorklistRow> Rows);

/// <summary>The worklist filter, bound from the query string.</summary>
public sealed class WorklistFilterInput
{
    /// <summary>Gets or sets the bank to show.</summary>
    public Guid? Institution { get; set; }

    /// <summary>Gets or sets the return type to show.</summary>
    public Guid? ReturnType { get; set; }

    /// <summary>Gets or sets a value indicating whether to show late returns only.</summary>
    public bool? Late { get; set; }

    /// <summary>Gets a value indicating whether only late returns are shown.</summary>
    public bool LateOnly => Late == true;

    /// <summary>Gets a value indicating whether any filter is set.</summary>
    public bool IsFiltered => Institution is not null || ReturnType is not null || LateOnly;
}
