using RegReturns.Application.Paging;

namespace RegReturns.UnitTests.Application;

public sealed class PagingTests
{
    [Theory]
    [InlineData(1, 25, 0, 25)]
    [InlineData(3, 10, 20, 10)]
    [InlineData(0, 10, 0, 10)]
    [InlineData(2, 500, 100, 100)]
    [InlineData(2, 0, 1, 1)]
    public void A_page_request_skips_whole_pages_of_a_bounded_size(int page, int pageSize, int skip, int take)
    {
        var request = new PageRequest(page, pageSize);

        request.Skip.ShouldBe(skip);
        request.Take.ShouldBe(take);
    }

    [Theory]
    [InlineData(0, 25, 0)]
    [InlineData(1, 25, 1)]
    [InlineData(25, 25, 1)]
    [InlineData(26, 25, 2)]
    public void Total_pages_round_up(int totalCount, int pageSize, int totalPages)
    {
        new PagedList<int>([], 1, pageSize, totalCount).TotalPages.ShouldBe(totalPages);
    }
}
