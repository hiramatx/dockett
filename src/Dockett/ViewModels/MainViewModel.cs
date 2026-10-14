using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dockett.Models;
using Dockett.Services;

namespace Dockett.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    TabViewModel? _watchedTab;

    /// <param name="startPaths">Optional folders for the left and right panes, e.g. from the command line. These win over the session.</param>
    /// <param name="session">The previous session to restore, if any.</param>
    /// <param name="settings">Live preferences; defaults when null (e.g. in tests).</param>
    public MainViewModel(IReadOnlyList<string>? startPaths = null, SessionState? session = null, SettingsViewModel? settings = null)
    {
        Settings = settings ?? new SettingsViewModel();
        Settings.PropertyChanged += OnSettingsChanged;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? home;
        var valid = (startPaths ?? []).Where(Directory.Exists).Select(Path.GetFullPath).ToList();
        if (valid.Count > 0)
            session = session is null ? null : session with { Left = null, Right = null, RightPaneActive = false };

        Left = RestorePane("Left", session?.Left, valid.ElementAtOrDefault(0) ?? home, Settings);
        Right = RestorePane("Right", session?.Right, valid.ElementAtOrDefault(1) ?? systemRoot, Settings);
        NavSections = FileSystemService.BuildNavSections();

        foreach (var pane in new[] { Left, Right })
        {
            pane.ActivationRequested += Activate;
            pane.PropertyChanged += OnPanePropertyChanged;
        }
        ActivePane = session?.RightPaneActive == true ? Right : Left;
        ActivePane.IsActive = true;
        IsPreviewVisible = session?.PreviewVisible ?? true;
        WatchActiveTab();
    }

    /// <summary>
    /// Rebuilds a pane from its saved tabs. A tab whose folder is gone opens the nearest folder above it that
    /// still exists; tabs on drives that are gone are dropped. With nothing left, the pane opens <paramref name="fallback"/>.
    /// </summary>
    static PaneViewModel RestorePane(string name, PaneState? saved, string fallback, SettingsViewModel settings)
    {
        var tabs = new List<TabState>();
        var selected = 0;
        if (saved is not null)
        {
            for (var i = 0; i < saved.Tabs.Count; i++)
            {
                if (SessionStore.NearestExisting(saved.Tabs[i].Path) is not { } path) continue;
                if (i == saved.SelectedIndex) selected = tabs.Count;
                tabs.Add(saved.Tabs[i] with { Path = path });
            }
        }
        return tabs.Count > 0
            ? new PaneViewModel(name, tabs, selected, settings)
            : new PaneViewModel(name, [fallback], settings);
    }

    /// <summary>The panes, tabs, and preview state to save; the window adds its own placement.</summary>
    public SessionState CaptureSession() => new()
    {
        Left = Left.Capture(),
        Right = Right.Capture(),
        RightPaneActive = ActivePane == Right,
        PreviewVisible = IsPreviewVisible,
    };

    /// <summary>Set by the window once it exists.</summary>
    public IUiHost? Host { get; set; }

    public SettingsViewModel Settings { get; }

    [RelayCommand]
    Task OpenSettings() => Host?.ShowSettingsAsync(Settings) ?? Task.CompletedTask;

    async void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Hidden files need a fresh listing; folder order only needs a re-sort.
        var reload = e.PropertyName == nameof(SettingsViewModel.ShowHidden);
        if (!reload && e.PropertyName != nameof(SettingsViewModel.FoldersFirst)) return;
        await Task.WhenAll(Left.Tabs.Concat(Right.Tabs).Select(t => t.ApplySettingsAsync(reload)));
    }

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
        if (Settings.ConfirmTransfers
            && !await Host.ConfirmAsync($"{verb} {Describe(items)}", $"{verb} {Describe(items)} to\n{target.CurrentPath}", verb))
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

        if (Settings.ConfirmDelete
            && !await Host.ConfirmAsync("Move to Recycle Bin", $"Move {Describe(items)} to the Recycle Bin?", "Delete", destructive: true))
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

    // ---- Clipboard ----

    [RelayCommand]
    Task CopyToClipboard() => PutOnClipboard(cut: false);

    [RelayCommand]
    Task CutToClipboard() => PutOnClipboard(cut: true);

    async Task PutOnClipboard(bool cut)
    {
        var items = ActiveTab.TargetItems;
        if (items.Count == 0) return;
        try
        {
            WindowsClipboard.SetFiles(Paths(items), cut);
        }
        catch (IOException e)
        {
            await Alert("Couldn't use the clipboard", e.Message);
            return;
        }
        MarkCut(cut ? Paths(items) : []);
        StatusMessage = $"{(cut ? "Cut" : "Copied")} {Describe(items)}";
    }

    /// <summary>Pastes files from the clipboard (from Dockett or Explorer) into <paramref name="targetFolder"/> or the active folder.</summary>
    [RelayCommand]
    async Task Paste(string? targetFolder)
    {
        if (Host is null) return;
        var target = targetFolder ?? ActiveTab.CurrentPath;

        (List<string> Paths, bool Cut)? clip;
        try
        {
            clip = WindowsClipboard.GetFiles();
        }
        catch (IOException e)
        {
            await Alert("Couldn't use the clipboard", e.Message);
            return;
        }
        if (clip is not { Paths.Count: > 0 } files)
        {
            StatusMessage = "There are no files on the clipboard";
            return;
        }
        if (files.Cut && files.Paths.All(p => SameFolder(Path.GetDirectoryName(p) ?? "", target)))
            return; // cut and pasted back where it came from

        var ok = await RunTool(() => files.Cut
            ? ShellOperations.MoveAsync(Host.OwnerHandle, files.Paths, target)
            : ShellOperations.CopyAsync(Host.OwnerHandle, files.Paths, target));
        if (ok && files.Cut)
        {
            // Like Explorer, a cut can only be pasted once.
            WindowsClipboard.Clear();
            MarkCut([]);
        }

        var label = files.Paths.Count == 1 ? $"“{Path.GetFileName(files.Paths[0])}”" : Format.Count(files.Paths.Count, "item");
        StatusMessage = ok ? $"Pasted {label}" : "Paste cancelled or incomplete";
        await RefreshFoldersShowing([target, .. files.Paths.Select(Path.GetDirectoryName).OfType<string>()]);
    }

    /// <summary>Fades rows that are on the clipboard as a cut, like Explorer.</summary>
    void MarkCut(IEnumerable<string> paths)
    {
        var set = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Left.Tabs.Concat(Right.Tabs).SelectMany(t => t.Items))
            item.IsCut = set.Contains(item.FullPath);
    }

    [RelayCommand]
    void CopyPath()
    {
        var items = ActiveTab.TargetItems;
        var text = items.Count == 0 ? ActiveTab.CurrentPath : string.Join(Environment.NewLine, Paths(items));
        CopyText(text, items.Count <= 1 ? "path" : "paths");
    }

    [RelayCommand]
    void CopyFolderPath() => CopyText(ActiveTab.CurrentPath, "path");

    void CopyText(string text, string what)
    {
        try
        {
            WindowsClipboard.SetText(text);
            StatusMessage = $"Copied {what}";
        }
        catch (IOException e)
        {
            StatusMessage = e.Message;
        }
    }

    // ---- Context menu actions ----

    [RelayCommand]
    Task OpenInNewTab(FileItem item) => ActivePane.OpenTabAsync(item.FullPath);

    [RelayCommand]
    async Task OpenInOtherPane(FileItem item)
    {
        var pane = InactivePane;
        if (await pane.SelectedTab.NavigateAsync(item.FullPath))
            Activate(pane);
    }

    [RelayCommand]
    void OpenWith(FileItem item)
    {
        if (Host is not null)
            ShellOperations.OpenWith(Host.OwnerHandle, item.FullPath);
    }

    [RelayCommand]
    void OpenTerminal(string? folder) => Launch(() => ShellOperations.OpenTerminal(folder ?? ActiveTab.CurrentPath));

    [RelayCommand]
    void RevealInExplorer(string? path) => Launch(() => ShellOperations.RevealInExplorer(path ?? ActiveTab.CurrentPath));

    [RelayCommand]
    void FolderProperties()
    {
        if (Host is not null)
            ShellOperations.ShowProperties(Host.OwnerHandle, ActiveTab.CurrentPath);
    }

    void Launch(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            StatusMessage = e.Message;
        }
    }

    // ---- Drag and drop ----

    /// <summary>Copies or moves dropped items into <paramref name="targetFolder"/>. Drops don't ask for confirmation.</summary>
    public async Task DropFilesAsync(IReadOnlyList<string> sources, string targetFolder, DropAction action)
    {
        if (action == DropAction.None || sources.Count == 0 || Host is null) return;

        var move = action == DropAction.Move;
        var names = sources.Select(Path.GetFileName).ToList();
        var label = names.Count == 1 ? $"“{names[0]}”" : Format.Count(names.Count, "item");
        var ok = await RunTool(() => move
            ? ShellOperations.MoveAsync(Host.OwnerHandle, sources, targetFolder)
            : ShellOperations.CopyAsync(Host.OwnerHandle, sources, targetFolder));

        var targetName = Path.GetFileName(Path.TrimEndingDirectorySeparator(targetFolder));
        if (targetName.Length == 0)
            targetName = targetFolder; // a drive root
        StatusMessage = ok
            ? $"{(move ? "Moved" : "Copied")} {label} to {targetName}"
            : $"{(move ? "Move" : "Copy")} cancelled or incomplete";

        var sourceFolders = sources.Select(s => Path.GetDirectoryName(s)).OfType<string>();
        await RefreshFoldersShowing([targetFolder, .. sourceFolders]);
    }

    /// <summary>Refreshes every tab showing one of <paramref name="folders"/>, e.g. after a drag to another app moved files away.</summary>
    public Task RefreshFoldersShowing(IEnumerable<string> folders)
    {
        var set = folders.Select(Path.TrimEndingDirectorySeparator).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Task.WhenAll(Left.Tabs.Concat(Right.Tabs)
            .Where(t => set.Contains(Path.TrimEndingDirectorySeparator(t.CurrentPath)))
            .Select(t => t.RefreshAsync()));
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
