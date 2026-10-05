namespace RegReturns.Application.Paging;

/// <summary>Which page of a list to return (ADR 0027): pages count from 1.</summary>
/// <param name="Page">The page number, from 1.</param>
/// <param name="PageSize">The number of items per page, from 1 to <see cref="MaxPageSize"/>.</param>
public sealed record PageRequest(int Page = 1, int PageSize = PageRequest.DefaultPageSize)
{
    /// <summary>The page size when the caller names none.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>The largest page size accepted.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Gets the number of items before this page, clamped to a valid page and size.</summary>
    public int Skip => (Math.Max(Page, 1) - 1) * Take;

    /// <summary>Gets the page size, clamped to 1 to <see cref="MaxPageSize"/>.</summary>
    public int Take => Math.Clamp(PageSize, 1, MaxPageSize);
}

/// <summary>One page of a list, with what a client needs to fetch the others.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items on this page.</param>
/// <param name="Page">The page number, from 1.</param>
/// <param name="PageSize">The page size.</param>
/// <param name="TotalCount">The number of items on all pages.</param>
public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    /// <summary>Gets the number of pages (0 when the list is empty).</summary>
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
