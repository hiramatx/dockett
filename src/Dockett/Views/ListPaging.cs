namespace Dockett.Views;

/// <summary>Where Page Up and Page Down move the focused row in a list of fixed-height rows, like Explorer.</summary>
public static class ListPaging
{
    /// <param name="current">Index of the focused row, or -1 if none.</param>
    /// <param name="count">Number of rows.</param>
    /// <param name="offset">Vertical scroll offset, in the same units as <paramref name="rowHeight"/>.</param>
    /// <param name="viewport">Visible height of the list.</param>
    /// <returns>The row to focus. The first press goes to the edge of what's visible; further presses move a page.</returns>
    public static int Target(int current, int count, double offset, double viewport, double rowHeight, bool down)
    {
        if (count == 0) return -1;
        if (rowHeight <= 0 || viewport <= 0) return down ? count - 1 : 0;

        var perPage = Math.Max(1, (int)(viewport / rowHeight) - 1);
        // Rows only partly in view don't count as visible.
        var firstVisible = (int)Math.Ceiling(offset / rowHeight - 0.001);
        var lastVisible = Math.Max(firstVisible, (int)Math.Floor((offset + viewport) / rowHeight + 0.001) - 1);

        int target;
        if (current < 0)
            target = down ? lastVisible : firstVisible;
        else if (down)
            target = current < lastVisible ? lastVisible : current + perPage;
        else
            target = current > firstVisible ? firstVisible : current - perPage;

        return Math.Clamp(target, 0, count - 1);
    }
}
