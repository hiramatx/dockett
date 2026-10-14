namespace Dockett.Models;

public enum NavIcon
{
    Home,
    Desktop,
    Documents,
    Downloads,
    Pictures,
    Drive,
    Network,
}

/// <summary>An entry in the navigation pane: a known folder or a drive.</summary>
public class NavItem
{
    public required string Label { get; init; }
    public required string Path { get; init; }
    public NavIcon Icon { get; init; }

    /// <summary>0..1 used fraction for drives; null hides the usage bar.</summary>
    public double? Usage { get; init; }

    public string? Detail { get; init; }

    public bool HasUsage => Usage is not null;
    public double UsagePercent => (Usage ?? 0) * 100;
}

public class NavSection
{
    public required string Title { get; init; }
    public required IReadOnlyList<NavItem> Items { get; init; }
}
