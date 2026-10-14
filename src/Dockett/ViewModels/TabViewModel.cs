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

/// <summary>One folder tab: its location, history, listing, filter, and sort. Refreshes itself when the folder changes.</summary>
public partial class TabViewModel : ViewModelBase, IDisposable
{
    readonly Stack<string> _back = new();
    readonly Stack<string> _forward = new();
    readonly FolderWatcher _watcher = new();
    readonly SynchronizationContext? _ui;
    List<FileItem> _all = [];
    int _loadVersion;
    bool _suppressFilterView;
    bool _disposed;

    public TabViewModel(string path, SortColumn sortBy = SortColumn.Name, bool sortAscending = true)
    {
        SortBy = sortBy;
        SortAscending = sortAscending;
        _ui = SynchronizationContext.Current;
        _watcher.Changed += OnFolderChanged;
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

    /// <summary>
    /// Reloads the folder, keeping the current selection unless <paramref name="selectName"/> says what to select.
    /// If the folder no longer exists, moves to the nearest folder above it that does.
    /// </summary>
    public Task RefreshAsync(string? selectName = null) =>
        Directory.Exists(CurrentPath) ? LoadAsync(CurrentPath, selectName, addToHistory: false) : GoToNearestExistingAsync();

    void OnFolderChanged()
    {
        if (_ui is null)
            _ = AutoRefreshAsync();
        else
            _ui.Post(_ => _ = AutoRefreshAsync(), null);
    }

    async Task AutoRefreshAsync()
    {
        // Never compete with a navigation in progress; it will show fresh contents anyway.
        if (_disposed || IsLoading) return;
        await RefreshAsync();
    }

    async Task GoToNearestExistingAsync()
    {
        for (var dir = Directory.GetParent(Path.TrimEndingDirectorySeparator(CurrentPath)); dir is not null; dir = dir.Parent)
        {
            if (dir.Exists && await NavigateAsync(dir.FullName))
                return;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher.Dispose();
    }

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

            if (samePlace && _all.Count > 0)
            {
                ErrorText = null;
                MergeRefresh(items, selectName);
                return true;
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
        Items.Clear();
        foreach (var item in BuildView())
            Items.Add(item);

        var select = selectName is null ? null : Items.FirstOrDefault(i => string.Equals(i.Name, selectName, StringComparison.OrdinalIgnoreCase));
        select ??= Items.FirstOrDefault();
        SelectedItems = [];
        CurrentItem = select;
        UpdateStatus();
        ItemsReloaded?.Invoke(select);
    }

    /// <summary>
    /// Applies a fresh listing of the same folder without rebuilding the list: unchanged rows keep their
    /// instances (and so their selection and compare marks), changed files update in place, and only
    /// added or removed rows touch the collection.
    /// </summary>
    void MergeRefresh(List<FileItem> fresh, string? selectName)
    {
        var old = new Dictionary<string, FileItem>(StringComparer.Ordinal);
        foreach (var item in _all)
            old[item.Name] = item;

        var current = CurrentItem;
        var currentIndex = current is null ? -1 : Items.IndexOf(current);
        var currentChanged = false;

        var all = new List<FileItem>(fresh.Count);
        foreach (var item in fresh)
        {
            if (old.TryGetValue(item.Name, out var existing) && existing.IsDirectory == item.IsDirectory)
            {
                if (existing.Size != item.Size || existing.Modified != item.Modified)
                {
                    existing.Size = item.Size;
                    existing.Modified = item.Modified;
                    currentChanged |= existing == current;
                }
                all.Add(existing);
            }
            else
            {
                all.Add(item);
            }
        }
        _all = all;

        var target = BuildView();
        var keep = new HashSet<FileItem>(target, ReferenceEqualityComparer.Instance);
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Items[i]))
                Items.RemoveAt(i);
        }
        for (var i = 0; i < target.Count; i++)
        {
            if (i < Items.Count && ReferenceEquals(Items[i], target[i])) continue;
            var from = IndexOfReference(Items, target[i], i + 1);
            if (from >= 0)
                Items.Move(from, i);
            else
                Items.Insert(i, target[i]);
        }

        SelectedItems = SelectedItems.Where(keep.Contains).ToList();
        if (selectName is not null)
        {
            var select = Items.FirstOrDefault(i => string.Equals(i.Name, selectName, StringComparison.OrdinalIgnoreCase)) ?? current;
            CurrentItem = select;
            ItemsReloaded?.Invoke(select);
        }
        else if (current is not null && !keep.Contains(current))
        {
            // The focused row went away: focus its neighbour.
            var neighbour = Items.Count == 0 ? null : Items[Math.Clamp(currentIndex, 0, Items.Count - 1)];
            CurrentItem = neighbour;
            ItemsReloaded?.Invoke(neighbour);
        }
        else if (CurrentItem != current)
        {
            CurrentItem = current; // the list may have cleared it while rows moved
        }
        else if (currentChanged)
        {
            OnPropertyChanged(nameof(CurrentItem)); // let the preview pick up the new size or contents
        }
        UpdateStatus();
    }

    static int IndexOfReference(IList<FileItem> list, FileItem item, int start)
    {
        for (var i = start; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
                return i;
        }
        return -1;
    }

    List<FileItem> BuildView()
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
        return sorted.ToList();
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

    partial void OnCurrentPathChanged(string value)
    {
        OnPropertyChanged(nameof(CanGoUp));
        _watcher.Watch(value);
    }
}
