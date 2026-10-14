using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dockett.Models;
using Dockett.Services;
using Dockett.ViewModels;

namespace Dockett.Views;

public partial class MainWindow : Window, IUiHost
{
    MainViewModel? _vm;
    GridLength _previewWidth = new(260);

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        foreach (var pane in new[] { LeftPane, RightPane })
        {
            pane.ItemActivated += OnItemActivated;
            pane.FilesDropped += OnFilesDropped;
            pane.DragCompleted += OnDragCompleted;
        }
        Opened += (_, _) => Dispatcher.UIThread.Post(() => ActivePaneView?.FocusList(), DispatcherPriority.Background);
        Opened += (_, _) => Dispatcher.UIThread.Post(HideBuiltInTitle, DispatcherPriority.Background);
    }

    public nint OwnerHandle => TryGetPlatformHandle()?.Handle ?? 0;

    FilePaneView? ActivePaneView => _vm is null ? null : _vm.ActivePane == _vm.Left ? LeftPane : RightPane;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = DataContext as MainViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyPreviewVisibility();
        }
    }

    void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsPreviewVisible):
                ApplyPreviewVisibility();
                break;
            case nameof(MainViewModel.IsEditingAddress) when _vm!.IsEditingAddress:
                Dispatcher.UIThread.Post(() =>
                {
                    AddressBox.Focus();
                    AddressBox.SelectAll();
                }, DispatcherPriority.Background);
                break;
        }
    }

    void ApplyPreviewVisibility()
    {
        var visible = _vm?.IsPreviewVisible ?? true;
        var column = MainGrid.ColumnDefinitions[6];
        if (!visible && column.Width.Value > 0)
            _previewWidth = column.Width;
        column.Width = visible ? _previewWidth : new GridLength(0);
        PreviewPanel.IsVisible = PreviewSplitter.IsVisible = PreviewLine.IsVisible = visible;
    }

    void OnItemActivated(FileItem item) => _vm?.OpenItemCommand.Execute(item);

    async void OnFilesDropped(IReadOnlyList<string> sources, string targetFolder, DropAction action)
    {
        if (_vm is null) return;
        // Let the OS drag loop finish before the shell shows any progress or conflict UI.
        await Task.Yield();
        await _vm.DropFilesAsync(sources, targetFolder, action);
        FocusActiveList();
    }

    async void OnDragCompleted(IReadOnlyList<string> sources)
    {
        if (_vm is not null)
            await _vm.RefreshFoldersShowing(sources.Select(Path.GetDirectoryName).OfType<string>());
    }

    void FocusActiveList() => Dispatcher.UIThread.Post(() => ActivePaneView?.FocusList(), DispatcherPriority.Background);

    /// <summary>
    /// Avalonia 12 draws the window title over the client area when it is extended into the title bar.
    /// Dockett draws its own, so hide the built-in one. Its visibility is template-bound, so a style can't override it.
    /// </summary>
    void HideBuiltInTitle()
    {
        Visual root = this;
        while (root.GetVisualParent() is { } parent)
            root = parent;
        foreach (var panel in root.GetVisualDescendants().OfType<Panel>().Where(p => p.Name == "PART_TitleTextPanel"))
            panel.IsVisible = false;
    }

    // ---- Keyboard ----

    void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        var mods = e.KeyModifiers;
        var inTextBox = FocusManager?.GetFocusedElement() is TextBox;

        if (inTextBox)
        {
            if (e.Key == Key.Escape && FocusManager?.GetFocusedElement() == SearchBox)
            {
                _vm.ActiveTab.Filter = "";
                FocusActiveList();
                e.Handled = true;
            }
            else if (e.Key == Key.Down && FocusManager?.GetFocusedElement() == SearchBox)
            {
                FocusActiveList();
                e.Handled = true;
            }
            return;
        }

        Action? action = (e.Key, mods) switch
        {
            (Key.Tab, KeyModifiers.None) => () => { _vm.SwitchPane(); FocusActiveList(); },
            (Key.Tab, KeyModifiers.Control) => () => { _vm.NextTabCommand.Execute(null); FocusActiveList(); },
            (Key.Tab, KeyModifiers.Control | KeyModifiers.Shift) => () => { _vm.PreviousTabCommand.Execute(null); FocusActiveList(); },
            (Key.F2, KeyModifiers.None) => () => Run(_vm.RenameCommand),
            (Key.F3, KeyModifiers.None) => () => _vm.TogglePreviewCommand.Execute(null),
            (Key.F5, KeyModifiers.None) => () => Run(_vm.CopyCommand),
            (Key.F6, KeyModifiers.None) => () => Run(_vm.MoveCommand),
            (Key.F7, KeyModifiers.None) => () => Run(_vm.NewFolderCommand),
            (Key.Delete, KeyModifiers.None) => () => Run(_vm.DeleteCommand),
            (Key.Enter, KeyModifiers.Alt) => () => _vm.PropertiesCommand.Execute(null),
            (Key.Left, KeyModifiers.Alt) => () => Run(_vm.GoBackCommand),
            (Key.Right, KeyModifiers.Alt) => () => Run(_vm.GoForwardCommand),
            (Key.Up, KeyModifiers.Alt) => () => Run(_vm.GoUpCommand),
            (Key.R, KeyModifiers.Control) => () => Run(_vm.RefreshCommand),
            (Key.T, KeyModifiers.Control) => () => Run(_vm.NewTabCommand),
            (Key.W, KeyModifiers.Control) => () => { _vm.CloseTabCommand.Execute(null); FocusActiveList(); },
            (Key.L, KeyModifiers.Control) or (Key.D, KeyModifiers.Alt) => () => _vm.BeginEditAddressCommand.Execute(null),
            (Key.F, KeyModifiers.Control) => () => SearchBox.Focus(),
            _ => null,
        };
        if (action is null) return;
        e.Handled = true;
        action();
    }

    async void Run(CommunityToolkit.Mvvm.Input.IAsyncRelayCommand command)
    {
        await command.ExecuteAsync(null);
        FocusActiveList();
    }

    void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = SubmitAddressAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _vm.CancelEditAddressCommand.Execute(null);
            FocusActiveList();
        }
    }

    async Task SubmitAddressAsync()
    {
        await _vm!.SubmitAddressCommand.ExecuteAsync(null);
        if (!_vm.IsEditingAddress)
            FocusActiveList();
    }

    void OnAddressLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null)
            _vm.IsEditingAddress = false;
    }

    // ---- IUiHost ----

    public async Task<string?> PromptAsync(string title, string message, string initialText, bool selectStem = false)
    {
        var dialog = new DialogWindow(title, message, "OK", initialText: initialText, selectStem: selectStem);
        var result = await dialog.ShowDialog<string?>(this);
        FocusActiveList();
        return result;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false)
    {
        var dialog = new DialogWindow(title, message, confirmText, destructive: destructive);
        var result = await dialog.ShowDialog<string?>(this);
        return result is not null;
    }

    public async Task AlertAsync(string title, string message)
    {
        var dialog = new DialogWindow(title, message, "OK", showCancel: false);
        await dialog.ShowDialog<string?>(this);
        FocusActiveList();
    }
}
