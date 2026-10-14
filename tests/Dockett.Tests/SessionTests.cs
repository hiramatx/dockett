using Dockett.Models;
using Dockett.Services;
using Dockett.ViewModels;

namespace Dockett.Tests;

public class SessionTests
{
    [Fact]
    public void Save_and_load_round_trip()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "nested", "session.json");
        var state = new SessionState
        {
            Left = new PaneState { Tabs = [new TabState { Path = @"C:\a", SortBy = SortColumn.Size, SortAscending = false }], SelectedIndex = 0 },
            RightPaneActive = true,
            PreviewVisible = false,
            Window = new WindowPlacement { X = 10, Y = 20, Width = 1200, Height = 700, Maximized = true, LeftShare = 0.6 },
        };

        SessionStore.Save(state, file);
        var loaded = SessionStore.Load(file);

        Assert.NotNull(loaded);
        Assert.Equal(SortColumn.Size, loaded.Left!.Tabs[0].SortBy);
        Assert.False(loaded.Left.Tabs[0].SortAscending);
        Assert.True(loaded.RightPaneActive);
        Assert.False(loaded.PreviewVisible);
        Assert.Equal(state.Window, loaded.Window);
        Assert.False(File.Exists(file + ".tmp"));
    }

    [Fact]
    public void Missing_or_corrupt_session_starts_fresh()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "session.json");
        Assert.Null(SessionStore.Load(file));

        File.WriteAllText(file, "{ not json");
        Assert.Null(SessionStore.Load(file));
    }

    [Fact]
    public void NearestExisting_walks_up_to_a_real_folder()
    {
        using var dir = new TempDir();
        Assert.Equal(dir.Path, SessionStore.NearestExisting(Path.Combine(dir.Path, "gone", "deeper")));
        Assert.Null(SessionStore.NearestExisting(@"Q:\surely\not\a\drive"));
    }

    [Fact]
    public async Task Restores_tabs_selection_sort_active_pane_and_preview()
    {
        using var dir = new TempDir();
        var one = dir.Dir("one");
        var two = dir.Dir("two");
        var session = new SessionState
        {
            Left = new PaneState
            {
                Tabs =
                [
                    new TabState { Path = one },
                    new TabState { Path = Path.Combine(two, "deleted"), SortBy = SortColumn.Modified, SortAscending = false },
                    new TabState { Path = @"Q:\no\such\drive" },
                ],
                SelectedIndex = 1,
            },
            Right = new PaneState { Tabs = [new TabState { Path = dir.Path }] },
            RightPaneActive = true,
            PreviewVisible = false,
        };

        var vm = new MainViewModel(null, session);
        await vm.InitializeAsync();

        Assert.Equal([one, two], vm.Left.Tabs.Select(t => t.CurrentPath));   // missing drive dropped
        Assert.Equal(two, vm.Left.SelectedTab.CurrentPath);                    // deleted folder -> parent, still selected
        Assert.Equal(SortColumn.Modified, vm.Left.SelectedTab.SortBy);
        Assert.False(vm.Left.SelectedTab.SortAscending);
        Assert.Same(vm.Right, vm.ActivePane);
        Assert.False(vm.IsPreviewVisible);

        var captured = vm.CaptureSession();
        Assert.Equal(1, captured.Left!.SelectedIndex);
        Assert.Equal(2, captured.Left.Tabs.Count);
        Assert.True(captured.RightPaneActive);
    }

    [Fact]
    public async Task Command_line_folders_win_over_the_session()
    {
        using var dir = new TempDir();
        var saved = dir.Dir("saved");
        var asked = dir.Dir("asked");
        var session = new SessionState
        {
            Left = new PaneState { Tabs = [new TabState { Path = saved }, new TabState { Path = saved }] },
            RightPaneActive = true,
            PreviewVisible = false,
        };

        var vm = new MainViewModel([asked], session);
        await vm.InitializeAsync();

        Assert.Equal([asked], vm.Left.Tabs.Select(t => t.CurrentPath));
        Assert.Same(vm.Left, vm.ActivePane);
        Assert.False(vm.IsPreviewVisible); // non-pane settings still come from the session
    }

    [Fact]
    public async Task Empty_session_uses_defaults()
    {
        var vm = new MainViewModel(null, new SessionState());
        await vm.InitializeAsync();

        Assert.Single(vm.Left.Tabs);
        Assert.Single(vm.Right.Tabs);
        Assert.True(vm.IsPreviewVisible);
    }
}
