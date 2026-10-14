using System.Diagnostics;
using Dockett.Services;
using Dockett.ViewModels;

namespace Dockett.Tests;

public class AutoRefreshTests
{
    static async Task<bool> WaitUntil(Func<bool> condition, int timeoutMs = 4000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                if (condition()) return true;
            }
            catch (InvalidOperationException)
            {
                // the collection changed while we looked; try again
            }
            await Task.Delay(50);
        }
        return condition();
    }

    [Fact]
    public async Task Watcher_coalesces_a_burst_into_one_notification()
    {
        using var dir = new TempDir();
        using var watcher = new FolderWatcher();
        var fired = 0;
        watcher.Changed += () => Interlocked.Increment(ref fired);
        watcher.Watch(dir.Path);

        for (var i = 0; i < 20; i++)
            dir.File($"f{i}.txt");

        Assert.True(await WaitUntil(() => fired > 0));
        await Task.Delay(600);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task Watcher_still_fires_while_changes_keep_coming()
    {
        using var dir = new TempDir();
        using var watcher = new FolderWatcher();
        var fired = 0;
        watcher.Changed += () => Interlocked.Increment(ref fired);
        watcher.Watch(dir.Path);

        // Write every 100 ms for 2.5 s: a pure debounce would never fire until the end.
        var file = Path.Combine(dir.Path, "growing.bin");
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 2500)
        {
            await File.AppendAllTextAsync(file, "x");
            await Task.Delay(100);
            if (sw.ElapsedMilliseconds > 1600)
                Assert.True(fired > 0, "should have fired at least once during continuous writes");
        }
    }

    [Fact]
    public async Task New_files_appear_and_existing_rows_keep_their_identity()
    {
        using var dir = new TempDir();
        dir.File("a.txt");
        dir.File("c.txt");
        var tab = new TabViewModel(dir.Path);
        await tab.InitializeAsync();
        var a = tab.Items[0];
        var c = tab.Items[1];

        dir.File("b.txt");

        Assert.True(await WaitUntil(() => tab.Items.Count == 3));
        Assert.Equal(["a.txt", "b.txt", "c.txt"], tab.Items.Select(i => i.Name));
        Assert.Same(a, tab.Items[0]);
        Assert.Same(c, tab.Items[2]);
        tab.Dispose();
    }

    [Fact]
    public async Task Changed_file_updates_in_place()
    {
        using var dir = new TempDir();
        var path = dir.File("log.txt", 10);
        var tab = new TabViewModel(dir.Path);
        await tab.InitializeAsync();
        var row = tab.Items.Single();

        await File.AppendAllTextAsync(path, new string('x', 990));

        Assert.True(await WaitUntil(() => row.Size == 1000));
        Assert.Same(row, tab.Items.Single());
        tab.Dispose();
    }

    [Fact]
    public async Task Deleting_the_focused_file_focuses_its_neighbour()
    {
        using var dir = new TempDir();
        dir.File("1.txt");
        var doomed = dir.File("2.txt");
        dir.File("3.txt");
        var tab = new TabViewModel(dir.Path);
        await tab.InitializeAsync();
        tab.CurrentItem = tab.Items[1];

        File.Delete(doomed);

        Assert.True(await WaitUntil(() => tab.Items.Count == 2));
        Assert.Equal("3.txt", tab.CurrentItem?.Name);
        tab.Dispose();
    }

    [Fact]
    public async Task Refresh_keeps_the_focused_row()
    {
        using var dir = new TempDir();
        dir.File("1.txt");
        dir.File("2.txt");
        var tab = new TabViewModel(dir.Path);
        await tab.InitializeAsync();
        var second = tab.Items[1];
        tab.CurrentItem = second;

        await tab.RefreshAsync();

        Assert.Same(second, tab.CurrentItem);
        tab.Dispose();
    }

    [Fact]
    public async Task Deleted_folder_moves_tab_to_nearest_existing_parent()
    {
        using var dir = new TempDir();
        var deep = dir.Dir(@"x\y");
        var tab = new TabViewModel(deep);
        await tab.InitializeAsync();

        Directory.Delete(Path.Combine(dir.Path, "x"), recursive: true);
        await tab.RefreshAsync();

        Assert.Equal(dir.Path, tab.CurrentPath);
        tab.Dispose();
    }
}
