using Dockett.Views;

namespace Dockett.Tests;

public class ListPagingTests
{
    // 10 rows of 26 px fit in a 260 px viewport.
    const double Row = 26, View = 260;

    [Theory]
    [InlineData(0, 0, 9)]     // first press: bottom of the visible page
    [InlineData(4, 0, 9)]
    [InlineData(9, 0, 18)]    // already at the bottom: move a page (one row of overlap)
    [InlineData(95, 2340, 99)] // clamped at the end
    [InlineData(-1, 0, 9)]
    public void Page_down(int current, double offset, int expected) =>
        Assert.Equal(expected, ListPaging.Target(current, 100, offset, View, Row, down: true));

    [Theory]
    [InlineData(18, 260, 10)] // first press: top of the visible page
    [InlineData(10, 260, 1)]  // already at the top: move a page
    [InlineData(3, 0, 0)]
    [InlineData(-1, 260, 10)]
    public void Page_up(int current, double offset, int expected) =>
        Assert.Equal(expected, ListPaging.Target(current, 100, offset, View, Row, down: false));

    [Fact]
    public void Partly_visible_rows_are_skipped()
    {
        // Scrolled 13 px: row 0 is cut off at the top and row 10 at the bottom, so rows 1-9 are fully visible.
        Assert.Equal(1, ListPaging.Target(5, 100, 13, View, Row, down: false));
        Assert.Equal(9, ListPaging.Target(5, 100, 13, View, Row, down: true));
    }

    [Fact]
    public void Empty_or_unmeasured_lists()
    {
        Assert.Equal(-1, ListPaging.Target(0, 0, 0, View, Row, down: true));
        Assert.Equal(4, ListPaging.Target(0, 5, 0, 0, Row, down: true));
        Assert.Equal(0, ListPaging.Target(3, 5, 0, 0, Row, down: false));
    }
}
