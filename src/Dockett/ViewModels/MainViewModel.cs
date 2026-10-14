using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dockett.Models;
using Dockett.Services;

namespace Dockett.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    TabViewModel? _watchedTab;

    /// <param name="startPaths">Optional folders for the left and right panes, e.g. from the command line.</param>
    public MainViewModel(IReadOnlyList<string>? startPaths = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? home;
        var valid = (startPaths ?? []).Where(Directory.Exists).Select(Path.GetFullPath).ToList();

        Left = new PaneViewModel("Left", [valid.ElementAtOrDefault(0) ?? home]);
        Right = new PaneViewModel("Right", [valid.ElementAtOrDefault(1) ?? systemRoot]);
        NavSections = FileSystemService.BuildNavSections();

        foreach (var pane in new[] { Left, Right })
        {
            pane.ActivationRequested += Activate;
            pane.PropertyChanged += OnPanePropertyChanged;
        }
        ActivePane = Left;
        Left.IsActive = true;
        WatchActiveTab();
    }

    /// <summary>Set by the window once it exists.</summary>
    public IUiHost? Host { get; set; }

    public PaneViewModel Left { get; }
    public PaneViewModel Right { get; }
    public IReadOnlyList<NavSection> NavSections { get; }
    public PreviewViewModel Preview { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ActiveTab), nameof(InactivePane))]
    public partial PaneViewModel ActivePane { get; private set; }

    public PaneViewModel InactivePane => ActivePane == Left ? Right : Left;
    public TabViewModel ActiveTab => ActivePane.SelectedTab;

    [ObservableProperty]
    public partial bool IsPreviewVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsEditingAddress { get; set; }

    [ObservableProperty]
    public partial string AddressText { get; set; } = "";

    /// <summary>Result of the last operation, shown in the bottom bar.</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public Task InitializeAsync() => Task.WhenAll(Left.InitializeAsync(), Right.InitializeAsync());

    public void Activate(PaneViewModel pane)
    {
        if (ActivePane == pane) return;
        ActivePane.IsActive = false;
        pane.IsActive = true;
        ActivePane = pane;
        IsEditingAddress = false;
        WatchActiveTab();
    }

    [RelayCommand]
    public void SwitchPane() => Activate(InactivePane);

    void OnPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaneViewModel.SelectedTab) && sender == ActivePane)
        {
            OnPropertyChanged(nameof(ActiveTab));
            WatchActiveTab();
        }
    }

    void WatchActiveTab()
    {
        if (_watchedTab is not null)
            _watchedTab.PropertyChanged -= OnActiveTabPropertyChanged;
        _watchedTab = ActiveTab;
        _watchedTab.PropertyChanged += OnActiveTabPropertyChanged;
        UpdatePreview();
    }

    void OnActiveTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.CurrentItem))
            UpdatePreview();
    }

    void UpdatePreview()
    {
        if (IsPreviewVisible)
            _ = Preview.ShowAsync(ActiveTab.CurrentItem);
    }

    partial void OnIsPreviewVisibleChanged(bool value) => _ = Preview.ShowAsync(value ? ActiveTab.CurrentItem : null);

    // ---- Navigation ----

    [RelayCommand]
    async Task NavigateTo(NavItem item)
    {
        await ActiveTab.NavigateAsync(item.Path);
    }

    [RelayCommand]
    async Task NavigateCrumb(Crumb crumb)
    {
        await ActiveTab.NavigateAsync(crumb.Path);
    }

    [RelayCommand]
    Task GoBack() => ActiveTab.GoBackAsync();

    [RelayCommand]
    Task GoForward() => ActiveTab.GoForwardAsync();

    [RelayCommand]
    Task GoUp() => ActiveTab.GoUpAsync();

    [RelayCommand]
    Task Refresh() => ActiveTab.RefreshAsync();

    [RelayCommand]
    void BeginEditAddress()
    {
        AddressText = ActiveTab.CurrentPath;
        IsEditingAddress = true;
    }

    [RelayCommand]
    void CancelEditAddress() => IsEditingAddress = false;

    [RelayCommand]
    async Task SubmitAddress()
    {
        var text = AddressText.Trim();
        if (text.Length == 0)
        {
            IsEditingAddress = false;
            return;
        }

        // A file path opens the file's folder with the file selected.
        if (File.Exists(text))
        {
            if (await ActiveTab.NavigateAsync(Path.GetDirectoryName(text)!, Path.GetFileName(text)))
                IsEditingAddress = false;
            return;
        }
        if (await ActiveTab.NavigateAsync(text))
            IsEditingAddress = false;
    }

    [RelayCommand]
    public async Task OpenItem(FileItem? item)
    {
        item ??= ActiveTab.CurrentItem;
        if (item is null) return;
        if (item.IsDirectory)
        {
            await ActiveTab.NavigateAsync(item.FullPath);
            return;
        }
        try
        {
            ShellOperations.Open(item.FullPath);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            await Alert("Couldn't open file", $"Windows couldn't open “{item.Name}”. {e.Message}");
        }
    }

    [RelayCommand]
    Task NewTab() => ActivePane.NewTab();

    [RelayCommand]
    void CloseTab() => ActivePane.CloseTab(null);

    [RelayCommand]
    void NextTab() => ActivePane.CycleTab(1);

    [RelayCommand]
    void PreviousTab() => ActivePane.CycleTab(-1);

    [RelayCommand]
    void TogglePreview() => IsPreviewVisible = !IsPreviewVisible;

    // ---- File tools ----

    [RelayCommand]
    Task Copy() => Transfer(move: false);

    [RelayCommand]
    Task Move() => Transfer(move: true);

    async Task Transfer(bool move)
    {
        var source = ActiveTab;
        var target = InactivePane.SelectedTab;
        var items = source.TargetItems;
        if (items.Count == 0 || Host is null) return;

        var verb = move ? "Move" : "Copy";
        if (move && SameFolder(source.CurrentPath, target.CurrentPath))
        {
            await Alert("Can't move here", "Both panes show the same folder. Open a different folder in the other pane first.");
            return;
        }
        if (!await Host.ConfirmAsync($"{verb} {Describe(items)}", $"{verb} {Describe(items)} to\n{target.CurrentPath}", verb))
            return;

        var ok = await RunTool(() => move
            ? ShellOperations.MoveAsync(Host.OwnerHandle, Paths(items), target.CurrentPath)
            : ShellOperations.CopyAsync(Host.OwnerHandle, Paths(items), target.CurrentPath));

        StatusMessage = ok ? $"{(move ? "Moved" : "Copied")} {Describe(items)}" : $"{verb} cancelled or incomplete";
        await RefreshFolders(source.CurrentPath, target.CurrentPath, selectInTarget: items[0].Name);
    }

    [RelayCommand]
    async Task Rename()
    {
        var tab = ActiveTab;
        var item = tab.TargetItems.FirstOrDefault();
        if (item is null || Host is null) return;

        var name = await Host.PromptAsync("Rename", $"New name for “{item.Name}”", item.Name, selectStem: !item.IsDirectory);
        if (string.IsNullOrWhiteSpace(name) || name == item.Name) return;
        if (!IsValidName(name))
        {
            await Alert("Can't use that name", "Names can't contain \\ / : * ? \" < > |");
            return;
        }

        if (await RunTool(() => ShellOperations.RenameAsync(Host.OwnerHandle, item.FullPath, name.Trim())))
            StatusMessage = $"Renamed to “{name.Trim()}”";
        await RefreshFolders(tab.CurrentPath, null, selectInSource: name.Trim());
    }

    [RelayCommand]
    async Task NewFolder()
    {
        var tab = ActiveTab;
        if (Host is null) return;

        var name = await Host.PromptAsync("New folder", $"Create a folder in {tab.CurrentPath}", FileSystemService.UniqueName(tab.CurrentPath, "New folder"));
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (!IsValidName(name))
        {
            await Alert("Can't use that name", "Names can't contain \\ / : * ? \" < > |");
            return;
        }
        var path = Path.Combine(tab.CurrentPath, name);
        if (Path.Exists(path))
        {
            await Alert("Name already used", $"“{name}” already exists here.");
            return;
        }

        try
        {
            Directory.CreateDirectory(path);
            StatusMessage = $"Created “{name}”";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Alert("Couldn't create folder", e.Message);
        }
        await RefreshFolders(tab.CurrentPath, null, selectInSource: name);
    }

    [RelayCommand]
    async Task Delete()
    {
        var tab = ActiveTab;
        var items = tab.TargetItems;
        if (items.Count == 0 || Host is null) return;

        if (!await Host.ConfirmAsync("Move to Recycle Bin", $"Move {Describe(items)} to the Recycle Bin?", "Delete", destructive: true))
            return;

        // Keep the cursor near where the deleted rows were.
        var index = tab.Items.IndexOf(items[0]);
        var neighbour = tab.Items.Where(i => !items.Contains(i)).Skip(Math.Max(0, index - 1)).FirstOrDefault()?.Name;

        var ok = await RunTool(() => ShellOperations.RecycleAsync(Host.OwnerHandle, Paths(items)));
        StatusMessage = ok ? $"Moved {Describe(items)} to the Recycle Bin" : "Delete cancelled or incomplete";
        await RefreshFolders(tab.CurrentPath, null, selectInSource: neighbour);
    }

    [RelayCommand]
    async Task Compress()
    {
        var tab = ActiveTab;
        var items = tab.TargetItems;
        if (items.Count == 0 || Host is null) return;

        var suggested = items.Count == 1 ? Path.GetFileNameWithoutExtension(items[0].Name) + ".zip" : tab.Header + ".zip";
        var name = await Host.PromptAsync("Compress", $"Zip {Describe(items)} as", FileSystemService.UniqueName(tab.CurrentPath, suggested), selectStem: true);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            name += ".zip";
        var zipPath = Path.Combine(tab.CurrentPath, name);
        if (File.Exists(zipPath))
        {
            await Alert("Name already used", $"“{name}” already exists here.");
            return;
        }

        StatusMessage = $"Compressing {Describe(items)}…";
        try
        {
            await ShellOperations.CompressAsync(Paths(items), zipPath);
            StatusMessage = $"Created “{name}”";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            StatusMessage = null;
            await Alert("Couldn't create zip", e.Message);
        }
        await RefreshFolders(tab.CurrentPath, null, selectInSource: name);
    }

    [RelayCommand]
    void Compare()
    {
        var left = Left.SelectedTab.Items;
        var right = Right.SelectedTab.Items;
        var rightByName = right.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
        var leftByName = left.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);

        var differences = 0;
        foreach (var item in left)
        {
            item.IsDifferent = !rightByName.TryGetValue(item.Name, out var other) || Differs(item, other);
            if (item.IsDifferent) differences++;
        }
        foreach (var item in right)
        {
            item.IsDifferent = !leftByName.TryGetValue(item.Name, out var other) || Differs(item, other);
            if (item.IsDifferent && !leftByName.ContainsKey(item.Name)) differences++;
        }

        StatusMessage = differences == 0
            ? "Both folders have the same items"
            : $"{Format.Count(differences, "difference")} marked";

        static bool Differs(FileItem a, FileItem b) =>
            a.IsDirectory != b.IsDirectory
            || (!a.IsDirectory && (a.Size != b.Size || Math.Abs((a.Modified - b.Modified).TotalSeconds) > 2));
    }

    [RelayCommand]
    void Properties()
    {
        if (Host is null) return;
        var path = ActiveTab.TargetItems.FirstOrDefault()?.FullPath ?? ActiveTab.CurrentPath;
        ShellOperations.ShowProperties(Host.OwnerHandle, path);
    }

    // ---- Helpers ----

    async Task RefreshFolders(string sourceFolder, string? targetFolder, string? selectInSource = null, string? selectInTarget = null)
    {
        var tasks = new List<Task>();
        foreach (var tab in Left.Tabs.Concat(Right.Tabs))
        {
            if (SameFolder(tab.CurrentPath, sourceFolder))
                tasks.Add(tab.RefreshAsync(tab == ActiveTab ? selectInSource : null));
            else if (targetFolder is not null && SameFolder(tab.CurrentPath, targetFolder))
                tasks.Add(tab.RefreshAsync(tab == InactivePane.SelectedTab ? selectInTarget : null));
        }
        await Task.WhenAll(tasks);
    }

    async Task<bool> RunTool(Func<Task<bool>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception e)
        {
            await Alert("Something went wrong", e.Message);
            return false;
        }
    }

    Task Alert(string title, string message) => Host?.AlertAsync(title, message) ?? Task.CompletedTask;

    static List<string> Paths(IEnumerable<FileItem> items) => items.Select(i => i.FullPath).ToList();

    static string Describe(IReadOnlyList<FileItem> items) =>
        items.Count == 1 ? $"“{items[0].Name}”" : Format.Count(items.Count, "item");

    static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    static bool IsValidName(string name) =>
        name.Trim().Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.Trim() is not "." and not "..";
}
