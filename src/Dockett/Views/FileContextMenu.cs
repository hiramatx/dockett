using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Dockett.Models;
using Dockett.Services;
using Dockett.ViewModels;

namespace Dockett.Views;

/// <summary>Builds the right-click menu for file rows and for a pane's empty space.</summary>
public static class FileContextMenu
{
    /// <param name="item">The row that was right-clicked, or null for empty space.</param>
    /// <param name="showMoreOptions">Opens the Windows shell menu for the selection.</param>
    public static ContextMenu Build(MainViewModel vm, FileItem? item, Action showMoreOptions)
    {
        // Wide enough that long labels never squeeze out the shortcut column.
        var menu = new ContextMenu { MinWidth = 280 };
        var items = new List<Control>();

        if (item is null)
        {
            items.Add(Item("Paste", "Icon.Paste", vm.PasteCommand, null, "Ctrl+V", enabled: WindowsClipboard.HasFiles));
            items.Add(Item("New folder", "Icon.FolderPlus", vm.NewFolderCommand, null, "F7"));
            items.Add(new Separator());
            items.Add(Item("Refresh", "Icon.Refresh", vm.RefreshCommand, null, "Ctrl+R"));
            items.Add(new Separator());
            items.Add(Item("Copy folder path", "Icon.Link", vm.CopyFolderPathCommand, null, null));
            items.Add(Item("Open in Terminal", "Icon.Terminal", vm.OpenTerminalCommand, null, null));
            items.Add(Item("Open in Explorer", "Icon.FolderOutline", vm.RevealInExplorerCommand, null, null));
            items.Add(new Separator());
            items.Add(Item("Properties", "Icon.Info", vm.FolderPropertiesCommand, null, null));
            menu.ItemsSource = items;
            return menu;
        }

        var many = vm.ActiveTab.TargetItems.Count > 1;
        // No shortcut text on Open or Properties: Avalonia displays Enter as "Return".
        var open = Item("Open", null, vm.OpenItemCommand, item, null);
        open.FontWeight = FontWeight.SemiBold;
        items.Add(open);
        if (item.IsDirectory && !many)
        {
            items.Add(Item("Open in new tab", "Icon.Plus", vm.OpenInNewTabCommand, item, null));
            items.Add(Item("Open in other pane", "Icon.Compare", vm.OpenInOtherPaneCommand, item, null));
        }
        else if (!item.IsDirectory && !many)
        {
            items.Add(Item("Open with…", null, vm.OpenWithCommand, item, null));
        }
        items.Add(new Separator());
        items.Add(Item("Cut", "Icon.Cut", vm.CutToClipboardCommand, null, "Ctrl+X"));
        items.Add(Item("Copy", "Icon.Copy", vm.CopyToClipboardCommand, null, "Ctrl+C"));
        if (item.IsDirectory && !many)
            items.Add(Item("Paste into folder", "Icon.Paste", vm.PasteCommand, item.FullPath, null, enabled: WindowsClipboard.HasFiles));
        items.Add(new Separator());
        items.Add(Item("Copy to other pane", null, vm.CopyCommand, null, "F5"));
        items.Add(Item("Move to other pane", "Icon.Move", vm.MoveCommand, null, "F6"));
        items.Add(new Separator());
        if (!many)
            items.Add(Item("Rename", "Icon.Rename", vm.RenameCommand, null, "F2"));
        items.Add(Item("Delete", "Icon.Delete", vm.DeleteCommand, null, "Delete"));
        items.Add(new Separator());
        items.Add(Item("Compress to zip", "Icon.Archive", vm.CompressCommand, null, null));
        items.Add(Item(many ? "Copy paths" : "Copy path", "Icon.Link", vm.CopyPathCommand, null, "Ctrl+Shift+C"));
        if (!many)
        {
            items.Add(Item("Open in Terminal", "Icon.Terminal", vm.OpenTerminalCommand,
                item.IsDirectory ? item.FullPath : Path.GetDirectoryName(item.FullPath), null));
            items.Add(Item("Show in Explorer", "Icon.FolderOutline", vm.RevealInExplorerCommand, item.FullPath, null));
        }
        items.Add(new Separator());
        if (!many)
            items.Add(Item("Properties", "Icon.Info", vm.PropertiesCommand, null, null));
        var more = new MenuItem { Header = "Show more options", InputGesture = null };
        ToolTip.SetTip(more, "The Windows menu, with extras like Open with and Send to. Shift+right-click opens it directly.");
        more.Click += (_, _) => showMoreOptions();
        items.Add(more);

        menu.ItemsSource = items;
        return menu;
    }

    static MenuItem Item(string header, string? icon, ICommand command, object? parameter, string? gesture, bool enabled = true)
    {
        var item = new MenuItem
        {
            Header = header,
            Command = command,
            CommandParameter = parameter,
            IsEnabled = enabled,
        };
        if (gesture is not null)
            item.InputGesture = KeyGesture.Parse(gesture);
        if (icon is not null && Application.Current?.TryGetResource(icon, null, out var geometry) == true && geometry is Geometry g)
            item.Icon = new PathIcon { Data = g, Width = 14, Height = 14 };
        return item;
    }
}
