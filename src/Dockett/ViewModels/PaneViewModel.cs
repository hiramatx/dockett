using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Dockett.ViewModels;

/// <summary>One side of the dual view: a strip of tabs, one of which is shown.</summary>
public partial class PaneViewModel : ViewModelBase
{
    public PaneViewModel(string name, IEnumerable<string> startPaths)
    {
        Name = name;
        foreach (var path in startPaths)
            Tabs.Add(new TabViewModel(path));
        SelectedTab = Tabs[0];
        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanCloseTabs));
    }

    public string Name { get; }

    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    [ObservableProperty]
    public partial TabViewModel SelectedTab { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>A pane always keeps at least one tab.</summary>
    public bool CanCloseTabs => Tabs.Count > 1;

    /// <summary>Raised when the user interacts with this pane so the window can make it active.</summary>
    public event Action<PaneViewModel>? ActivationRequested;

    public void RequestActivation() => ActivationRequested?.Invoke(this);

    public Task InitializeAsync() => Task.WhenAll(Tabs.Select(t => t.InitializeAsync()));

    [RelayCommand]
    void SelectTab(TabViewModel tab)
    {
        SelectedTab = tab;
        RequestActivation();
    }

    [RelayCommand]
    public Task NewTab() => OpenTabAsync(SelectedTab.CurrentPath);

    /// <summary>Opens <paramref name="path"/> in a new tab next to the current one.</summary>
    public async Task OpenTabAsync(string path)
    {
        var tab = new TabViewModel(path);
        Tabs.Insert(Tabs.IndexOf(SelectedTab) + 1, tab);
        SelectedTab = tab;
        RequestActivation();
        await tab.InitializeAsync();
    }

    [RelayCommand]
    public void CloseTab(TabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (Tabs.Count <= 1) return;
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        tab.Dispose();
        if (SelectedTab == tab || !Tabs.Contains(SelectedTab))
            SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
    }

    partial void OnSelectedTabChanged(TabViewModel oldValue, TabViewModel newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false; // null on first assignment
        newValue.IsSelected = true;
    }

    public void CycleTab(int delta)
    {
        var index = (Tabs.IndexOf(SelectedTab) + delta + Tabs.Count) % Tabs.Count;
        SelectedTab = Tabs[index];
    }
}
