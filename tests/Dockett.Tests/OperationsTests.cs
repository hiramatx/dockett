using Dockett.Services;
using Dockett.ViewModels;

namespace Dockett.Tests;

public class OperationsTests
{
    [Fact]
    public void UniqueName_appends_a_counter_before_the_extension()
    {
        using var dir = new TempDir();
        dir.File("a.zip");
        dir.File("a (2).zip");
        dir.Dir("New folder");

        Assert.Equal("a (3).zip", FileSystemService.UniqueName(dir.Path, "a.zip"));
        Assert.Equal("New folder (2)", FileSystemService.UniqueName(dir.Path, "New folder"));
        Assert.Equal("fresh.txt", FileSystemService.UniqueName(dir.Path, "fresh.txt"));
    }

    [Fact]
    public async Task Shell_copy_move_and_rename_work()
    {
        using var dir = new TempDir();
        var src = dir.Dir("src");
        var dst = dir.Dir("dst");
        var file = dir.File(@"src\data.bin", 64);
        dir.File(@"src\nested\inner.txt");

        Assert.True(await ShellOperations.CopyAsync(0, [file, Path.Combine(src, "nested")], dst));
        Assert.True(File.Exists(Path.Combine(dst, "data.bin")));
        Assert.True(File.Exists(Path.Combine(dst, "nested", "inner.txt")));
        Assert.True(File.Exists(file));

        Assert.True(await ShellOperations.RenameAsync(0, Path.Combine(dst, "data.bin"), "renamed.bin"));
        Assert.True(File.Exists(Path.Combine(dst, "renamed.bin")));

        Assert.True(await ShellOperations.MoveAsync(0, [file], Path.Combine(dst, "nested")));
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(dst, "nested", "data.bin")));
    }

    [Fact]
    public async Task Copying_into_the_same_folder_makes_a_copy()
    {
        using var dir = new TempDir();
        var file = dir.File("doc.txt");

        Assert.True(await ShellOperations.CopyAsync(0, [file], dir.Path));

        Assert.Equal(2, Directory.GetFiles(dir.Path).Length);
    }

    [Fact]
    public async Task Compress_keeps_folder_structure()
    {
        using var dir = new TempDir();
        var folder = dir.Dir("proj");
        dir.File(@"proj\a.txt");
        dir.File(@"proj\sub\b.txt");
        var loose = dir.File("loose.txt");
        var zip = Path.Combine(dir.Path, "out.zip");

        await ShellOperations.CompressAsync([folder, loose], zip);

        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        Assert.Equal(["loose.txt", "proj/a.txt", "proj/sub/b.txt"], archive.Entries.Select(e => e.FullName).Order());
    }

    [Fact]
    public async Task Compare_marks_missing_and_changed_files()
    {
        using var left = new TempDir();
        using var right = new TempDir();
        left.File("same.txt", 5);
        right.File("same.txt", 5);
        File.SetLastWriteTime(Path.Combine(right.Path, "same.txt"), File.GetLastWriteTime(Path.Combine(left.Path, "same.txt")));
        left.File("changed.txt", 5);
        right.File("changed.txt", 9);
        left.File("only-left.txt");
        right.File("only-right.txt");

        var vm = new MainViewModel();
        await vm.Left.SelectedTab.NavigateAsync(left.Path);
        await vm.Right.SelectedTab.NavigateAsync(right.Path);
        vm.CompareCommand.Execute(null);

        var leftMarked = vm.Left.SelectedTab.Items.Where(i => i.IsDifferent).Select(i => i.Name).Order();
        var rightMarked = vm.Right.SelectedTab.Items.Where(i => i.IsDifferent).Select(i => i.Name).Order();
        Assert.Equal(["changed.txt", "only-left.txt"], leftMarked);
        Assert.Equal(["changed.txt", "only-right.txt"], rightMarked);
        Assert.Equal("3 differences marked", vm.StatusMessage);
    }
}
