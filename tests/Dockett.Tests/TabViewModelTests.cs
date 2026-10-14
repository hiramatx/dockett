using Dockett.ViewModels;

namespace Dockett.Tests;

public class TabViewModelTests
{
    [Fact]
    public async Task Lists_folders_first_then_files_by_name()
    {
        using var dir = new TempDir();
        dir.File("b.txt");
        dir.File("A.txt");
        dir.Dir("zeta");
        dir.Dir("alpha");

        var tab = new TabViewModel(dir.Path);
        await tab.InitializeAsync();

        Assert.Equal(["alpha", "zeta", "A.txt", "b.txt"], tab.Items.Select(i => i.Name));
        Assert.Equal("alpha", tab.CurrentItem?.Name);
    }

    [Fact]
    public async Task Sorting_by_size_descending_keeps_folders_on_top()
    {
        using var dir = new TempDir();
        dir.File("small.bin", 10);
        dir.File("big.bin", 1000);
        dir.Dir("folder");

        var tab = new TabViewModel(dir.Path);
        await tab.InitializeAsync();
        tab.ToggleSort(SortColumn.Size);
        tab.ToggleSort(SortColumn.Size);

        Assert.False(tab.SortAscending);
        Assert.Equal(["folder", "big.bin", "small.bin"], tab.Items.Select(i => i.Name));
        Assert.EndsWith("↓", tab.SizeHeader);
    }

    [Fact]
    public async Task Filter_narrows_items_and_resets_on_navigation()
    {
        using var dir = new TempDir();
        dir.File("report.pdf");
        dir.File("notes.md");
        var sub = dir.Dir("sub");

        var tab = new TabViewModel(dir.Path);
        await tab.InitializeAsync();
        tab.Filter = "NOTE";
        Assert.Equal(["notes.md"], tab.Items.Select(i => i.Name));

        await tab.NavigateAsync(sub);
        Assert.Equal("", tab.Filter);
    }

    [Fact]
    public async Task Up_selects_the_folder_you_came_from_and_history_works()
    {
        using var dir = new TempDir();
        dir.Dir("one");
        var two = dir.Dir("two");

        var tab = new TabViewModel(dir.Path);
        await tab.InitializeAsync();
        Assert.False(tab.CanGoBack);

        await tab.NavigateAsync(two);
        Assert.True(tab.CanGoBack);

        await tab.GoUpAsync();
        Assert.Equal(dir.Path, tab.CurrentPath);
        Assert.Equal("two", tab.CurrentItem?.Name);

        await tab.GoBackAsync();
        Assert.Equal(two, tab.CurrentPath);
        Assert.True(tab.CanGoForward);

        await tab.GoForwardAsync();
        Assert.Equal(dir.Path, tab.CurrentPath);
    }

    [Fact]
    public async Task Missing_folder_shows_error_and_stays_put()
    {
        using var dir = new TempDir();
        var tab = new TabViewModel(dir.Path);
        await tab.InitializeAsync();

        var ok = await tab.NavigateAsync(Path.Combine(dir.Path, "nope"));

        Assert.False(ok);
        Assert.Equal(dir.Path, tab.CurrentPath);
        Assert.NotNull(tab.ErrorText);
        Assert.False(tab.CanGoBack);
    }

    [Fact]
    public async Task Breadcrumbs_run_from_drive_to_current_folder()
    {
        using var dir = new TempDir();
        var tab = new TabViewModel(dir.Path);
        await tab.InitializeAsync();

        Assert.Equal(Path.GetPathRoot(dir.Path)!.TrimEnd('\\'), tab.Breadcrumbs[0].Name);
        Assert.Equal(dir.Path, tab.Breadcrumbs[^1].Path);
    }
}
