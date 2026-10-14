using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dockett.Models;
using Dockett.Services;
using Dockett.ViewModels;

namespace Dockett.Views;

public partial class FilePaneView : UserControl
{
    const double DragThreshold = 6;

    PaneViewModel? _pane;
    TabViewModel? _tab;

    // Drag source state
    PointerPressedEventArgs? _dragPress;
    Point _dragOrigin;
    FileItem? _collapseSelectionTo;
    bool _dragging;

    // Drop target state
    ListBoxItem? _dropRow;

    // Context menu state
    bool _shiftRightClick;

    public FilePaneView()
    {
        InitializeComponent();
        // Any click or focus inside the pane makes it the active one, even if a child handles the event.
        AddHandler(PointerPressedEvent, (_, _) => _pane?.RequestActivation(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, _) => _pane?.RequestActivation(), RoutingStrategies.Bubble, handledEventsToo: true);

        List.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);
        List.AddHandler(PointerMovedEvent, OnListPointerMoved, RoutingStrategies.Tunnel);
        List.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);

        AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>Raised on double-click or Enter.</summary>
    public event Action<FileItem>? ItemActivated;

    /// <summary>Raised when files are dropped on this pane: sources, target folder, and what to do.</summary>
    public event Action<IReadOnlyList<string>, string, DropAction>? FilesDropped;

    /// <summary>Raised when a drag that started here was accepted somewhere, so source folders can refresh.</summary>
    public event Action<IReadOnlyList<string>>? DragCompleted;

    public void FocusList()
    {
        var item = List.SelectedItem is { } selected ? List.ContainerFromItem(selected) : null;
        if (item is not null)
            item.Focus(NavigationMethod.Tab);
        else
            List.Focus(NavigationMethod.Tab);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_pane is not null)
            _pane.PropertyChanged -= OnPanePropertyChanged;
        _pane = DataContext as PaneViewModel;
        if (_pane is not null)
            _pane.PropertyChanged += OnPanePropertyChanged;
        WatchTab();
    }

    void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaneViewModel.SelectedTab))
            WatchTab();
    }

    void WatchTab()
    {
        if (_tab is not null)
            _tab.ItemsReloaded -= OnItemsReloaded;
        _tab = _pane?.SelectedTab;
        if (_tab is not null)
        {
            _tab.ItemsReloaded += OnItemsReloaded;
            Dispatcher.UIThread.Post(() => OnItemsReloaded(_tab.CurrentItem), DispatcherPriority.Background);
        }
    }

    void OnItemsReloaded(FileItem? select)
    {
        // Let the ListBox create containers for the new items before selecting and scrolling.
        Dispatcher.UIThread.Post(() =>
        {
            if (select is null) return;
            List.SelectedItem = select;
            List.ScrollIntoView(select);
            if (_pane?.IsActive == true && (IsKeyboardFocusWithin || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is null))
                FocusList();
        }, DispatcherPriority.Background);
    }

    void OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        _tab?.SetSelection(List.SelectedItems?.OfType<FileItem>().ToList() ?? []);

    void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control c && c.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is FileItem item)
            ItemActivated?.Invoke(item);
    }

    async void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (_tab is null || e.KeyModifiers != KeyModifiers.None) return;
        switch (e.Key)
        {
            case Key.Enter when _tab.CurrentItem is { } item:
                e.Handled = true;
                ItemActivated?.Invoke(item);
                break;
            case Key.Back:
                e.Handled = true;
                await _tab.GoUpAsync();
                break;
        }
    }

    void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_pane is null || (sender as Control)?.DataContext is not TabViewModel tab) return;
        var point = e.GetCurrentPoint(this).Properties;
        if (point.IsMiddleButtonPressed)
            _pane.CloseTab(tab);
        else if (point.IsLeftButtonPressed)
            _pane.SelectTabCommand.Execute(tab);
    }

    // ---- Drag source ----

    static FileItem? ItemAt(object? source) =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as FileItem;

    void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragPress = null;
        _collapseSelectionTo = null;
        var properties = e.GetCurrentPoint(List).Properties;
        if (properties.IsRightButtonPressed)
        {
            _shiftRightClick = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            // Right-clicking inside the selection keeps it, so the menu acts on all selected rows.
            if (ItemAt(e.Source) is { } clicked && List.SelectedItems?.Contains(clicked) == true)
                e.Handled = true;
            return;
        }
        if (!properties.IsLeftButtonPressed || ItemAt(e.Source) is not { } item)
            return;

        _dragPress = e;
        _dragOrigin = e.GetPosition(List);

        // Pressing on part of a multi-selection keeps the selection so it can be dragged as a group;
        // a plain click (no drag) collapses it on release, like Explorer.
        if (e.KeyModifiers == KeyModifiers.None && List.SelectedItems is { Count: > 1 } selected && selected.Contains(item))
        {
            _collapseSelectionTo = item;
            e.Handled = true;
        }
    }

    async void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragPress is null || _dragging) return;
        if (!e.GetCurrentPoint(List).Properties.IsLeftButtonPressed)
        {
            _dragPress = null;
            return;
        }
        var delta = e.GetPosition(List) - _dragOrigin;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;

        var press = _dragPress;
        _dragPress = null;
        _collapseSelectionTo = null;
        await StartDragAsync(press);
    }

    void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_collapseSelectionTo is { } item && !_dragging)
        {
            List.SelectedItems?.Clear();
            List.SelectedItem = item;
        }
        _collapseSelectionTo = null;
        _dragPress = null;
    }

    async Task StartDragAsync(PointerPressedEventArgs press)
    {
        var items = List.SelectedItems?.OfType<FileItem>().ToList() ?? [];
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (items.Count == 0 || storage is null) return;

        _dragging = true;
        try
        {
            var data = new DataTransfer();
            var paths = new List<string>();
            foreach (var item in items)
            {
                IStorageItem? storageItem = item.IsDirectory
                    ? await storage.TryGetFolderFromPathAsync(item.FullPath)
                    : await storage.TryGetFileFromPathAsync(item.FullPath);
                if (storageItem is null) continue;
                data.Add(DataTransferItem.CreateFile(storageItem));
                paths.Add(item.FullPath);
            }
            if (paths.Count == 0) return;

            var effect = await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy | DragDropEffects.Move);
            if (effect != DragDropEffects.None)
                DragCompleted?.Invoke(paths);
        }
        finally
        {
            _dragging = false;
        }
    }

    // ---- Drop target ----

    static List<string> DroppedPaths(DragEventArgs e) =>
        e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];

    /// <summary>A folder row under the pointer, or else the folder this pane shows.</summary>
    (string? Folder, ListBoxItem? Row) DropTargetAt(DragEventArgs e, IReadOnlyList<string> sources)
    {
        // Hit-test the pointer position; the event's Source isn't reliably the element under the pointer.
        var row = (this.InputHitTest(e.GetPosition(this)) as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (row?.DataContext is FileItem { IsDirectory: true } folder
            && !sources.Contains(folder.FullPath, StringComparer.OrdinalIgnoreCase))
            return (folder.FullPath, row);
        return (_tab is { ErrorText: null } tab ? tab.CurrentPath : null, null);
    }

    DropAction Evaluate(DragEventArgs e, out string? folder, out ListBoxItem? row, out List<string> sources)
    {
        sources = DroppedPaths(e);
        (folder, row) = DropTargetAt(e, sources);
        if (folder is null) return DropAction.None;

        var action = DropRules.Choose(sources, folder, e.KeyModifiers.HasFlag(KeyModifiers.Control), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        // Respect what the drag source allows (some apps only offer copy).
        if (action == DropAction.Move && !e.DragEffects.HasFlag(DragDropEffects.Move))
            action = e.DragEffects.HasFlag(DragDropEffects.Copy) ? DropAction.Copy : DropAction.None;
        if (action == DropAction.Copy && !e.DragEffects.HasFlag(DragDropEffects.Copy))
            action = DropAction.None;
        return action;
    }

    void OnDragOver(object? sender, DragEventArgs e)
    {
        var action = Evaluate(e, out _, out var row, out _);
        e.DragEffects = ToEffects(action);
        ShowDropTarget(action == DropAction.None ? null : row, action != DropAction.None && row is null);
        e.Handled = true;
    }

    void OnDragLeave(object? sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between child elements; only clear once the pointer leaves the pane.
        if (!new Rect(Bounds.Size).Contains(e.GetPosition(this)))
            ShowDropTarget(null, false);
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        var action = Evaluate(e, out var folder, out _, out var sources);
        ShowDropTarget(null, false);
        e.DragEffects = ToEffects(action);
        e.Handled = true;
        if (action != DropAction.None && folder is not null)
        {
            _pane?.RequestActivation();
            FilesDropped?.Invoke(sources, folder, action);
        }
    }

    void ShowDropTarget(ListBoxItem? row, bool wholePane)
    {
        if (_dropRow != row)
        {
            _dropRow?.Classes.Remove("dropTarget");
            row?.Classes.Add("dropTarget");
            _dropRow = row;
        }
        DropOverlay.IsVisible = wholePane;
    }

    static DragDropEffects ToEffects(DropAction action) => action switch
    {
        DropAction.Copy => DragDropEffects.Copy,
        DropAction.Move => DragDropEffects.Move,
        _ => DragDropEffects.None,
    };

    // ---- Context menu ----

    void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_tab is null || TopLevel.GetTopLevel(this)?.DataContext is not MainViewModel vm) return;
        e.Handled = true;
        _pane?.RequestActivation();

        // Mouse: the row under the pointer. Keyboard (Menu key, Shift+F10): the focused row.
        var byPointer = e.TryGetPosition(List, out var position);
        var item = byPointer ? ItemAt(List.InputHitTest(position)) : _tab.CurrentItem;
        var shift = byPointer && _shiftRightClick;
        _shiftRightClick = false;

        if (item is null)
        {
            List.SelectedItems?.Clear();
        }
        else if (List.SelectedItems?.Contains(item) != true)
        {
            List.SelectedItems?.Clear();
            List.SelectedItem = item;
        }
        _tab.CurrentItem = item ?? _tab.CurrentItem;

        var container = item is null ? null : List.ContainerFromItem(item) as Control;
        var screen = byPointer
            ? List.PointToScreen(position)
            : container?.PointToScreen(new Point(8, container.Bounds.Height)) ?? List.PointToScreen(new Point(8, 8));

        if (shift && item is not null)
        {
            // Like Explorer on Windows 11: Shift+right-click goes straight to the full Windows menu.
            ShowShellMenu(screen, extended: true);
            return;
        }

        var menu = FileContextMenu.Build(vm, item, () =>
            Dispatcher.UIThread.Post(() => ShowShellMenu(screen, extended: false), DispatcherPriority.Background));
        menu.Closed += (_, _) => FocusList();
        if (byPointer)
        {
            menu.Placement = PlacementMode.Pointer;
            menu.Open(List);
        }
        else
        {
            menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
            menu.PlacementTarget = container ?? List;
            menu.Open(container ?? List);
        }
    }

    void ShowShellMenu(PixelPoint screen, bool extended)
    {
        var paths = _tab?.TargetItems.Select(i => i.FullPath).ToList() ?? [];
        var owner = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? 0;
        try
        {
            ShellContextMenu.Show(owner, paths, screen.X, screen.Y, extended);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or ArgumentException or UnauthorizedAccessException)
        {
            // The shell couldn't build a menu for these items; nothing useful to show.
            System.Diagnostics.Debug.WriteLine(e);
        }
        FocusList();
    }
}
