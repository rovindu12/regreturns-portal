using Microsoft.AspNetCore.Http;

using RegReturns.Api.Paging;

namespace RegReturns.UnitTests.Api;

public sealed class PagingLinksTests
{
    [Fact]
    public void A_middle_page_links_to_first_previous_next_and_last()
    {
        var header = PagingLinks.Build(Request("/v1/submissions", "?status=Draft&page=3&pageSize=10"), page: 3, pageSize: 10, totalPages: 5);

        header.ShouldBe(
            "</v1/submissions?status=Draft&page=1&pageSize=10>; rel=\"first\", "
            + "</v1/submissions?status=Draft&page=2&pageSize=10>; rel=\"prev\", "
            + "</v1/submissions?status=Draft&page=4&pageSize=10>; rel=\"next\", "
            + "</v1/submissions?status=Draft&page=5&pageSize=10>; rel=\"last\"");
    }

    [Fact]
    public void The_first_page_has_no_previous_and_the_last_no_next()
    {
        var first = PagingLinks.Build(Request("/v1/submissions"), page: 1, pageSize: 25, totalPages: 2);
        var last = PagingLinks.Build(Request("/v1/submissions"), page: 2, pageSize: 25, totalPages: 2);

        first.ShouldNotContain("rel=\"prev\"");
        first.ShouldContain("page=2&pageSize=25>; rel=\"next\"");
        last.ShouldNotContain("rel=\"next\"");
        last.ShouldContain("page=1&pageSize=25>; rel=\"prev\"");
    }

    [Fact]
    public void An_empty_list_still_has_a_first_and_last_page()
    {
        var header = PagingLinks.Build(Request("/v1/submissions"), page: 1, pageSize: 25, totalPages: 0);

        header.ShouldBe("</v1/submissions?page=1&pageSize=25>; rel=\"first\", </v1/submissions?page=1&pageSize=25>; rel=\"last\"");
    }

    [Fact]
    public void A_page_past_the_end_points_back_to_the_last_page()
    {
        var header = PagingLinks.Build(Request("/v1/submissions", "?page=9"), page: 9, pageSize: 25, totalPages: 3);

        header.ShouldContain("page=3&pageSize=25>; rel=\"prev\"");
        header.ShouldNotContain("rel=\"next\"");
    }

    [Fact]
    public void Links_keep_the_path_base_and_encode_values()
    {
        var request = Request("/v1/submissions", "?returnType=M%26A");
        request.PathBase = "/api";

        var header = PagingLinks.Build(request, page: 1, pageSize: 25, totalPages: 1);

        header.ShouldStartWith("</api/v1/submissions?returnType=M%26A&page=1&pageSize=25>");
    }

    private static HttpRequest Request(string path, string query = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query.Length == 0 ? null : query);
        return context.Request;
    }
}
