using Avalonia.Controls;
using Dockett.Models;
using Dockett.ViewModels;
using Dockett.Views;

namespace Dockett.Tests;

public class ContextMenuTests
{
    static List<string> Headers(ContextMenu menu) =>
        menu.ItemsSource!.OfType<MenuItem>().Select(i => i.Header?.ToString() ?? "").ToList();

    [Fact]
    public async Task Every_menu_variant_builds()
    {
        using var dir = new TempDir();
        dir.Dir("folder");
        dir.File("file.txt");
        var vm = new MainViewModel([dir.Path]);
        await vm.InitializeAsync();
        var tab = vm.Left.SelectedTab;
        var folder = tab.Items.Single(i => i.IsDirectory);
        var file = tab.Items.Single(i => !i.IsDirectory);

        // Building runs KeyGesture.Parse on every shortcut, which throws on a bad key name.
        var empty = FileContextMenu.Build(vm, null, () => { });
        Assert.Contains("New folder", Headers(empty));

        tab.CurrentItem = folder;
        var folderMenu = Headers(FileContextMenu.Build(vm, folder, () => { }));
        Assert.Contains("Open in new tab", folderMenu);
        Assert.Contains("Paste into folder", folderMenu);

        tab.CurrentItem = file;
        var fileMenu = Headers(FileContextMenu.Build(vm, file, () => { }));
        Assert.Contains("Open with…", fileMenu);
        Assert.Contains("Show more options", fileMenu);

        tab.SetSelection([folder, file]);
        var many = Headers(FileContextMenu.Build(vm, file, () => { }));
        Assert.DoesNotContain("Rename", many);
        Assert.Contains("Copy paths", many);
    }
}
