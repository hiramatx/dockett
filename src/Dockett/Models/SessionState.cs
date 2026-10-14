using Dockett.ViewModels;

namespace Dockett.Models;

/// <summary>What Dockett restores on the next start. Saved as JSON; unknown or missing fields fall back to defaults.</summary>
public sealed record SessionState
{
    public int Version { get; init; } = 1;
    public PaneState? Left { get; init; }
    public PaneState? Right { get; init; }
    public bool RightPaneActive { get; init; }
    public bool PreviewVisible { get; init; } = true;
    public WindowPlacement? Window { get; init; }
}

public sealed record PaneState
{
    public List<TabState> Tabs { get; init; } = [];
    public int SelectedIndex { get; init; }
}

public sealed record TabState
{
    public required string Path { get; init; }
    public SortColumn SortBy { get; init; } = SortColumn.Name;
    public bool SortAscending { get; init; } = true;
}

public sealed record WindowPlacement
{
    public int X { get; init; }
    public int Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public bool Maximized { get; init; }
    public double NavWidth { get; init; }
    public double PreviewWidth { get; init; }

    /// <summary>Left pane's share of the space the two file panes split, 0..1.</summary>
    public double LeftShare { get; init; } = 0.5;
}
