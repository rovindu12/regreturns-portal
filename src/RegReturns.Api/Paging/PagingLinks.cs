using System.Globalization;
using System.Text;

using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Primitives;

namespace RegReturns.Api.Paging;

/// <summary>
/// Builds the RFC 8288 <c>Link</c> header of a paged list (ADR 0027): <c>first</c>, <c>prev</c>, <c>next</c> and
/// <c>last</c>, each the request's own path and query with only <c>page</c> and <c>pageSize</c> replaced. The targets
/// are relative references, so they stay right behind a reverse proxy.
/// </summary>
internal static class PagingLinks
{
    /// <summary>The query parameter holding the page number.</summary>
    public const string PageParameter = "page";

    /// <summary>The query parameter holding the page size.</summary>
    public const string PageSizeParameter = "pageSize";

    /// <summary>Returns the header value.</summary>
    /// <param name="request">The request that asked for the page.</param>
    /// <param name="page">The page returned.</param>
    /// <param name="pageSize">The page size used.</param>
    /// <param name="totalPages">The number of pages (0 for an empty list, which still has a first and last page).</param>
    /// <returns>The <c>Link</c> header value.</returns>
    public static string Build(HttpRequest request, int page, int pageSize, int totalPages)
    {
        ArgumentNullException.ThrowIfNull(request);
        var last = Math.Max(totalPages, 1);
        var links = new List<(string Rel, int Page)> { ("first", 1) };
        if (page > 1)
        {
            links.Add(("prev", Math.Min(page - 1, last)));
        }

        if (page < totalPages)
        {
            links.Add(("next", page + 1));
        }

        links.Add(("last", last));

        var header = new StringBuilder();
        foreach (var (rel, target) in links)
        {
            if (header.Length > 0)
            {
                header.Append(", ");
            }

            header.Append('<').Append(Target(request, target, pageSize)).Append(">; rel=\"").Append(rel).Append('"');
        }

        return header.ToString();
    }

    private static string Target(HttpRequest request, int page, int pageSize)
    {
        var query = new QueryBuilder();
        foreach (var (key, values) in request.Query)
        {
            if (!string.Equals(key, PageParameter, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, PageSizeParameter, StringComparison.OrdinalIgnoreCase))
            {
                query.Add(key, Values(values));
            }
        }

        query.Add(PageParameter, page.ToString(CultureInfo.InvariantCulture));
        query.Add(PageSizeParameter, pageSize.ToString(CultureInfo.InvariantCulture));
        return string.Concat(request.PathBase.ToUriComponent(), request.Path.ToUriComponent(), query.ToQueryString().ToUriComponent());
    }

    private static IEnumerable<string> Values(StringValues values) => values.Where(v => v is not null).Select(v => v!);
}
