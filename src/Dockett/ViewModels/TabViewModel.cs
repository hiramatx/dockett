using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dockett.Models;
using Dockett.Services;

namespace Dockett.ViewModels;

public enum SortColumn
{
    Name,
    Size,
    Modified,
}

public record Crumb(string Name, string Path);

/// <summary>One folder tab: its location, history, listing, filter, and sort.</summary>
public partial class TabViewModel : ViewModelBase
{
    readonly Stack<string> _back = new();
    readonly Stack<string> _forward = new();
    List<FileItem> _all = [];
    int _loadVersion;
    bool _suppressFilterView;

    public TabViewModel(string path)
    {
        CurrentPath = path;
    }

    public ObservableCollection<FileItem> Items { get; } = [];
    public ObservableCollection<Crumb> Breadcrumbs { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Header))]
    public partial string CurrentPath { get; private set; }

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(NameHeader), nameof(SizeHeader), nameof(ModifiedHeader))]
    public partial SortColumn SortBy { get; private set; } = SortColumn.Name;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(NameHeader), nameof(SizeHeader), nameof(ModifiedHeader))]
    public partial bool SortAscending { get; private set; } = true;

    /// <summary>The focused row, used by the preview panel.</summary>
    [ObservableProperty]
    public partial FileItem? CurrentItem { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "";

    [ObservableProperty]
    public partial string? ErrorText { get; private set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>True for the tab its pane is showing.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Selected rows, pushed in by the view.</summary>
    public IReadOnlyList<FileItem> SelectedItems { get; private set; } = [];

    /// <summary>Raised after a reload with the item the view should select and scroll to.</summary>
    public event Action<FileItem?>? ItemsReloaded;

    public string Header
    {
        get
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(CurrentPath));
            return string.IsNullOrEmpty(name) ? CurrentPath.TrimEnd('\\') : name;
        }
    }

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public bool CanGoUp => Directory.GetParent(Path.TrimEndingDirectorySeparator(CurrentPath)) is not null;

    public string NameHeader => "Name" + Arrow(SortColumn.Name);
    public string SizeHeader => "Size" + Arrow(SortColumn.Size);
    public string ModifiedHeader => "Modified" + Arrow(SortColumn.Modified);

    string Arrow(SortColumn column) => SortBy != column ? "" : SortAscending ? "  ↑" : "  ↓";

    /// <summary>Paths acted on by tools: the selection, or the focused row when nothing is selected.</summary>
    public IReadOnlyList<FileItem> TargetItems =>
        SelectedItems.Count > 0 ? SelectedItems : CurrentItem is null ? [] : [CurrentItem];

    public void SetSelection(IReadOnlyList<FileItem> items)
    {
        SelectedItems = items;
        UpdateStatus();
    }

    public Task<bool> NavigateAsync(string path, string? selectName = null) => LoadAsync(path, selectName, addToHistory: true);

    public Task InitializeAsync() => LoadAsync(CurrentPath, null, addToHistory: false);

    public async Task GoBackAsync()
    {
        if (_back.Count == 0) return;
        var from = CurrentPath;
        if (await LoadAsync(_back.Peek(), null, addToHistory: false))
        {
            _back.Pop();
            _forward.Push(from);
            OnHistoryChanged();
        }
    }

    public async Task GoForwardAsync()
    {
        if (_forward.Count == 0) return;
        var from = CurrentPath;
        if (await LoadAsync(_forward.Peek(), null, addToHistory: false))
        {
            _forward.Pop();
            _back.Push(from);
            OnHistoryChanged();
        }
    }

    public async Task GoUpAsync()
    {
        var trimmed = Path.TrimEndingDirectorySeparator(CurrentPath);
        var parent = Directory.GetParent(trimmed);
        if (parent is not null)
            await NavigateAsync(parent.FullName, selectName: Path.GetFileName(trimmed));
    }

    public Task RefreshAsync(string? selectName = null) =>
        LoadAsync(CurrentPath, selectName ?? CurrentItem?.Name, addToHistory: false);

    [RelayCommand]
    public void ToggleSort(SortColumn column)
    {
        if (SortBy == column)
            SortAscending = !SortAscending;
        else
        {
            SortBy = column;
            SortAscending = true;
        }
        ApplyView(CurrentItem?.Name);
    }

    partial void OnFilterChanged(string value)
    {
        if (!_suppressFilterView)
            ApplyView(CurrentItem?.Name);
    }

    async Task<bool> LoadAsync(string path, string? selectName, bool addToHistory)
    {
        path = NormalizePath(path);
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            var showHidden = false;
            var items = await Task.Run(() => FileSystemService.List(path, showHidden));
            if (version != _loadVersion) return false; // a newer navigation won

            var samePlace = string.Equals(path, CurrentPath, StringComparison.OrdinalIgnoreCase);
            if (addToHistory && !samePlace)
            {
                _back.Push(CurrentPath);
                _forward.Clear();
                OnHistoryChanged();
            }

            _all = items;
            CurrentPath = path;
            ErrorText = null;
            RebuildBreadcrumbs();
            if (!samePlace)
            {
                _suppressFilterView = true;
                Filter = "";
                _suppressFilterView = false;
            }
            ApplyView(selectName);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            if (version == _loadVersion)
                ErrorText = e is UnauthorizedAccessException
                    ? $"You don't have access to {path}."
                    : $"Couldn't open {path}. {e.Message}";
            return false;
        }
        finally
        {
            if (version == _loadVersion)
                IsLoading = false;
        }
    }

    static string NormalizePath(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (path.Length == 2 && path[1] == ':')
            path += "\\";
        return Path.GetFullPath(path);
    }

    void ApplyView(string? selectName)
    {
        IEnumerable<FileItem> view = _all;
        if (!string.IsNullOrWhiteSpace(Filter))
            view = view.Where(i => i.Name.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase));

        Func<FileItem, IComparable> key = SortBy switch
        {
            SortColumn.Size => i => i.Size,
            SortColumn.Modified => i => i.Modified,
            _ => i => i.Name,
        };
        var folders = view.Where(i => i.IsDirectory);
        var files = view.Where(i => !i.IsDirectory);
        // Folders sort by name when sorting by size, since they have no size.
        var folderKey = SortBy == SortColumn.Size ? (i => i.Name) : key;
        var comparer = Comparer<IComparable>.Create((a, b) =>
            a is string sa && b is string sb ? StringComparer.CurrentCultureIgnoreCase.Compare(sa, sb) : a.CompareTo(b));
        var sorted = SortAscending
            ? folders.OrderBy(folderKey, comparer).Concat(files.OrderBy(key, comparer))
            : folders.OrderByDescending(folderKey, comparer).Concat(files.OrderByDescending(key, comparer));

        Items.Clear();
        foreach (var item in sorted)
            Items.Add(item);

        var select = selectName is null ? null : Items.FirstOrDefault(i => string.Equals(i.Name, selectName, StringComparison.OrdinalIgnoreCase));
        select ??= Items.FirstOrDefault();
        SelectedItems = [];
        CurrentItem = select;
        UpdateStatus();
        ItemsReloaded?.Invoke(select);
    }

    void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        var crumbs = new List<Crumb>();
        for (var dir = new DirectoryInfo(CurrentPath); dir is not null; dir = dir.Parent)
            crumbs.Add(new Crumb(dir.Parent is null ? dir.FullName.TrimEnd('\\') : dir.Name, dir.FullName));
        crumbs.Reverse();
        foreach (var crumb in crumbs)
            Breadcrumbs.Add(crumb);
    }

    void UpdateStatus()
    {
        var free = FileSystemService.FreeSpaceText(CurrentPath);
        string text;
        if (SelectedItems.Count > 1)
        {
            var bytes = SelectedItems.Where(i => !i.IsDirectory).Sum(i => i.Size);
            text = $"{SelectedItems.Count} of {Format.Count(Items.Count, "item")} selected · {Format.Size(bytes)}";
        }
        else
        {
            text = Format.Count(Items.Count, "item");
            if (Items.Count != _all.Count)
                text += $" (filtered from {_all.Count:N0})";
        }
        StatusText = free is null ? text : $"{text} · {free}";
    }

    void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
    }

    partial void OnCurrentPathChanged(string value) => OnPropertyChanged(nameof(CanGoUp));
}
