using KPI.Application.Common;
using KPI.Application.Issues;

namespace KPI.Tests;

public class PagedResultTests
{
    // The screens offer 10, 20, 30, 100 and 200 rows per page.
    [Theory]
    [InlineData(10, 10)]
    [InlineData(100, 100)]
    [InlineData(200, 200)]
    [InlineData(500, 200)]   // anything bigger is capped, not refused
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public void The_page_size_is_kept_between_1_and_200(int asked, int expected) =>
        Assert.Equal(expected, PagedResult<int>.ClampSize(asked));

    [Fact]
    public async Task A_list_can_be_asked_for_200_rows_a_page()
    {
        var w = await TestWorld.CreateAsync();

        var page = await w.Issues.ListAsync(new IssueQuery(PageSize: 200), w.ItA.Id);

        Assert.Equal(200, page.PageSize);
    }
}
