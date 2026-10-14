using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dockett.Models;
using Dockett.ViewModels;

namespace Dockett.Views;

public partial class FilePaneView : UserControl
{
    PaneViewModel? _pane;
    TabViewModel? _tab;

    public FilePaneView()
    {
        InitializeComponent();
        // Any click or focus inside the pane makes it the active one, even if a child handles the event.
        AddHandler(PointerPressedEvent, (_, _) => _pane?.RequestActivation(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, _) => _pane?.RequestActivation(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Raised on double-click or Enter.</summary>
    public event Action<FileItem>? ItemActivated;

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
}
